namespace FoosballArena.Services;

using FoosballArena.Models;

/// <summary>
/// Shared room state + the match state machine.
/// All mutation happens under _gate. Methods ending in "Locked" assume the
/// lock is already held. Events are raised OUTSIDE the lock.
/// </summary>
public class RoomManager
{
    public const int MaxPlayersPerRoom = 40;

    private static readonly TimeSpan Countdown = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MatchLength = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan GoalReset = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Intermission = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private readonly Dictionary<string, Room> _rooms = new();

    private DateTime _lastTick = DateTime.UtcNow;

    public event Action? RoomsChanged;

    // ---------- queries ----------

    public List<RoomSummary> GetSummaries()
    {
        lock (_gate)
        {
            return _rooms.Values
                .OrderBy(r => r.CreatedAt)
                .Select(r => new RoomSummary(r.Code, r.HostAlias, r.Phase,
                                             r.PlayerCount, r.MaxPlayers, r.SpectatorCount,
                                             r.ScoreA, r.ScoreB))
                .ToList();
        }
    }

    public RoomInfo? GetInfo(string code)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(code, out var r)) return null;
            var aliases = r.Participants.ToDictionary(p => p.Token, p => p.Alias);
            var queued = r.PlayerQueue.ToHashSet();
            return new RoomInfo(
            r.Code, r.HostAlias, r.HostToken, r.Phase, r.MaxPlayers,
            r.ScoreA, r.ScoreB, DisplayedSecond(r, DateTime.UtcNow),
            r.Participants.Select(p => new ParticipantInfo(
                p.Alias, p.Kind, p.Team, queued.Contains(p.Token))).ToList(),
            r.PlayerQueue.Count,
            r.Slots.Values.OrderBy(s => s.Team).ThenBy(s => s.LineY).ThenBy(s => s.ZoneMin)
                .Select(s => new SlotInfo(s.Token, aliases[s.Token], s.Team, s.Line,
                                          s.ZoneMin, s.ZoneMax, s.X))
                .ToList());
        }
    }

    // ---------- commands ----------

    public (string Code, string Token) CreateRoom(string alias)
    {
        string code, token;
        lock (_gate)
        {
            code = GenerateCodeLocked();
            token = Guid.NewGuid().ToString("N");
            var room = new Room(code, alias, token);
            room.Participants.Add(new RoomParticipant(token, alias, ParticipantKind.Player, Team.A));
            _rooms[code] = room;
        }
        RaiseChanged();
        return (code, token);
    }

    public JoinResult JoinRoom(string code, string alias, ParticipantKind wantedKind)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(code, out var room))
                return JoinResult.Fail("Room not found — it may have closed.");

            var kind = wantedKind;
            var midMatch = room.Phase is RoomPhase.Active or RoomPhase.Countdown or RoomPhase.GoalReset;

            // Can't join a running match as a player → spectator + queue for next one
            if (kind == ParticipantKind.Player && midMatch)
                kind = ParticipantKind.Spectator;

            if (kind == ParticipantKind.Player && room.PlayerCount >= room.MaxPlayers)
                return JoinResult.Fail($"Room is full ({room.MaxPlayers} players).");

            var team = kind == ParticipantKind.Player
                ? (room.CountTeam(Team.A) <= room.CountTeam(Team.B) ? Team.A : Team.B)
                : Team.None;

            var token = Guid.NewGuid().ToString("N");
            room.Participants.Add(new RoomParticipant(token, alias, kind, team));
            if (kind == ParticipantKind.Spectator && wantedKind == ParticipantKind.Player && midMatch)
                room.PlayerQueue.Enqueue(token);

            return JoinResult.Ok(token);
        }
    }

    /// <summary>Presence ping from the room page. No event — LastSeen isn't displayed.</summary>
    public void Heartbeat(string code, string token)
    {
        lock (_gate)
        {
            if (_rooms.TryGetValue(code, out var room))
            {
                var me = room.Participants.FirstOrDefault(p => p.Token == token);
                if (me is not null) me.LastSeen = DateTime.UtcNow;
            }
        }
    }

    public void LeaveRoom(string code, string token)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(code, out var room)) return;
            room.Participants.RemoveAll(p => p.Token == token);
            RebuildSlotsLocked(room);
            var remaining = room.PlayerQueue.Where(t => t != token).ToList();
            room.PlayerQueue.Clear();
            foreach (var t in remaining) room.PlayerQueue.Enqueue(t);
            if (room.Participants.Count == 0) _rooms.Remove(code);
        }
        RaiseChanged();
    }

    public JoinResult ForceStart(string code, string token)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(code, out var room)) return JoinResult.Fail("Room not found.");
            if (room.HostToken != token) return JoinResult.Fail("Only the host can force-start.");
            if (room.Phase != RoomPhase.Waiting) return JoinResult.Fail("Match already starting or running.");
            if (room.PlayerCount < 2) return JoinResult.Fail("Need at least 2 players to start.");
            StartCountdownLocked(room, DateTime.UtcNow);
        }
        RaiseChanged();
        return JoinResult.Ok("");
    }

    /// <summary>Temporary scoring hook (Session 4 replaces it with real physics).</summary>
    public JoinResult ScoreGoal(string code, Team team)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(code, out var room)) return JoinResult.Fail("Room not found.");
            if (room.Phase != RoomPhase.Active) return JoinResult.Fail("Match is not active.");
            if (team == Team.A) room.ScoreA++; else room.ScoreB++;
            room.Phase = RoomPhase.GoalReset;
            room.PhaseEndsAt = DateTime.UtcNow + GoalReset;  // MatchEndsAt untouched: clock keeps running
        }
        RaiseChanged();
        return JoinResult.Ok("");
    }

    // ---------- engine + sweeper hooks (called by background services) ----------

    public void TickAll()
    {
        bool changed = false;
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var dt = Math.Min((now - _lastTick).TotalSeconds, 0.25); // clamp: avoids a jump after debugger pauses
            _lastTick = now;

            foreach (var room in _rooms.Values.ToList())
            {
                IntegrateLocked(room, dt);
                changed |= AdvanceLocked(room, now);
            }
        }
        if (changed) RaiseChanged();
    }

    public void SweepStale()
    {
        bool changed = false;
        lock (_gate)
        {
            var cutoff = DateTime.UtcNow - StaleAfter;
            foreach (var room in _rooms.Values.ToList())
            {
                var before = room.Participants.Count;
                room.Participants.RemoveAll(p => p.LastSeen < cutoff);
                RebuildSlotsLocked(room);
                var alive = room.Participants.Select(p => p.Token).ToHashSet();
                var stillQueued = room.PlayerQueue.Where(alive.Contains).ToList();
                room.PlayerQueue.Clear();
                foreach (var t in stillQueued) room.PlayerQueue.Enqueue(t);

                if (room.Participants.Count == 0) { _rooms.Remove(room.Code); changed = true; }
                else if (room.Participants.Count != before) changed = true;
            }
        }
        if (changed) RaiseChanged();
    }

    // ---------- state machine (lock held) ----------

    private bool AdvanceLocked(Room room, DateTime now)
    {
        var transitioned = false;
        switch (room.Phase)
        {
            case RoomPhase.Waiting:
                if (room.CountTeam(Team.A) >= 3 && room.CountTeam(Team.B) >= 3)
                { StartCountdownLocked(room, now); transitioned = true; }
                break;

            case RoomPhase.Countdown:
                if (now >= room.PhaseEndsAt)
                { room.Phase = RoomPhase.Active; room.MatchEndsAt = now + MatchLength; transitioned = true; }
                break;

            case RoomPhase.Active:
                if (now >= room.MatchEndsAt)
                { room.Phase = RoomPhase.Finished; room.PhaseEndsAt = now + Intermission; transitioned = true; }
                break;

            case RoomPhase.GoalReset:
                if (now >= room.PhaseEndsAt)
                { room.Phase = RoomPhase.Active; transitioned = true; }  // clock was never stopped
                break;

            case RoomPhase.Finished:
                if (now >= room.PhaseEndsAt)
                { PromoteQueueLocked(room); StartCountdownLocked(room, now); transitioned = true; }
                break;
        }

        // Raise the UI event at most once per displayed second — a wall clock
        // doesn't need 5 updates/sec, and 40 clients × 5/sec would churn circuits.
        var shown = DisplayedSecond(room, now);
        // Movement throttle: raise if anyone slid 3+ units since the last raise
        var moved = room.Slots.Values.Any(s => Math.Abs(s.X - s.LastRaisedX) >= 3f);
        if (moved)
            foreach (var s in room.Slots.Values)
                s.LastRaisedX = s.X;
        var changed = transitioned || shown != room.LastShownSecond || moved;
        room.LastShownSecond = shown;
        return changed;
    }

    private static void StartCountdownLocked(Room room, DateTime now)
    {
        RebuildSlotsLocked(room);
        room.ScoreA = 0;
        room.ScoreB = 0;
        room.Phase = RoomPhase.Countdown;
        room.PhaseEndsAt = now + Countdown;
    }

    private void PromoteQueueLocked(Room room)
    {
        var waiting = room.PlayerQueue.ToList();
        room.PlayerQueue.Clear();
        foreach (var token in waiting)
        {
            var p = room.Participants.FirstOrDefault(x => x.Token == token);
            if (p is null) continue; // ghost got swept while waiting
            if (room.PlayerCount >= room.MaxPlayers) { room.PlayerQueue.Enqueue(token); continue; }
            p.Kind = ParticipantKind.Player;
            p.Team = room.CountTeam(Team.A) <= room.CountTeam(Team.B) ? Team.A : Team.B;
        }
    }

    private static int DisplayedSecond(Room room, DateTime now)
    {
        DateTime? end = room.Phase switch
        {
            RoomPhase.Countdown or RoomPhase.GoalReset or RoomPhase.Finished => room.PhaseEndsAt,
            RoomPhase.Active => room.MatchEndsAt,
            _ => null,
        };
        if (end is null) return 0;
        var left = end.Value - now;
        return left <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(left.TotalSeconds);
    }

    private string GenerateCodeLocked()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        while (true)
        {
            var code = new string(Enumerable.Range(0, 4)
                .Select(_ => alphabet[Random.Shared.Next(alphabet.Length)]).ToArray());
            if (!_rooms.ContainsKey(code)) return code;
        }
    }

    /// <summary>Input: set my lateral movement direction. Called per button-press, not per frame.</summary>
    public void SetMove(string code, string token, double dir)
    {
        lock (_gate)
        {
            if (_rooms.TryGetValue(code, out var room) &&
                room.Slots.TryGetValue(token, out var slot))
            {
                slot.MoveDir = Math.Clamp(dir, -1.0, 1.0);
            }
        }
    }

    /// <summary>Recompute the whole formation (join order → lines → tiled zones). Keeps X where valid.</summary>
    private static void RebuildSlotsLocked(Room room)
    {
        var previousX = room.Slots.ToDictionary(kv => kv.Key, kv => kv.Value.X);
        room.Slots.Clear();

        foreach (var kv in Formation.Assign(room.Participants))
        {
            if (previousX.TryGetValue(kv.Key, out var x))
                kv.Value.X = Math.Clamp(x, kv.Value.ZoneMin, kv.Value.ZoneMax);
            room.Slots[kv.Key] = kv.Value;
        }
    }

    private static void IntegrateLocked(Room room, double dtSeconds)
    {
        // Positioning is allowed while lining up (countdown) and during play
        if (room.Phase is not (RoomPhase.Active or RoomPhase.Countdown or RoomPhase.GoalReset))
            return;

        foreach (var s in room.Slots.Values)
        {
            if (s.MoveDir == 0) continue;
            s.X = Math.Clamp((float)(s.X + s.MoveDir * Pitch.PlayerSpeed * dtSeconds), s.ZoneMin, s.ZoneMax);
        }
    }

    private void RaiseChanged() => RoomsChanged?.Invoke();
}