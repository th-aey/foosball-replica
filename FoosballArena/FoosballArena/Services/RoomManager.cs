namespace FoosballArena.Services;

using FoosballArena.Models;

/// <summary>
/// Registered as a SINGLETON: every user's circuit talks to the same instance,
/// so everybody sees the same rooms. Multiple users mutate concurrently,
/// so every access goes through one lock.
/// </summary>
public class RoomManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Room> _rooms = new();

    /// <summary>Raised after any room changes. UI subscribes to re-render.</summary>
    public event Action? RoomsChanged;

    // ---------- queries (always return snapshots) ----------

    public List<RoomSummary> GetSummaries()
    {
        lock (_gate)
        {
            return _rooms.Values
                .OrderBy(r => r.CreatedAt)
                .Select(r => new RoomSummary(r.Code, r.HostAlias, r.Phase,
                                             r.PlayerCount, r.MaxPlayers, r.SpectatorCount))
                .ToList();
        }
    }

    public RoomInfo? GetInfo(string code)
    {
        lock (_gate)
        {
            return _rooms.TryGetValue(code, out var r) ? Snapshot(r) : null;
        }
    }

    // ---------- commands ----------

    public string CreateRoom(string hostAlias)
    {
        string code;
        lock (_gate)
        {
            code = GenerateCodeLocked();
            var room = new Room(code, hostAlias);
            room.Participants.Add(new RoomParticipant(hostAlias, ParticipantKind.Player, Team.A));
            _rooms[code] = room;
        }
        RaiseChanged();
        return code;
    }

    public JoinResult JoinRoom(string code, string alias, ParticipantKind kind)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(code, out var room))
                return JoinResult.Fail("Room not found — it may have closed.");

            if (kind == ParticipantKind.Player && room.PlayerCount >= room.MaxPlayers)
                return JoinResult.Fail($"Room is full ({room.MaxPlayers} players).");

            var team = kind == ParticipantKind.Player
                ? (room.CountTeam(Team.A) <= room.CountTeam(Team.B) ? Team.A : Team.B)
                : Team.None;

            room.Participants.Add(new RoomParticipant(alias, kind, team));
        }
        RaiseChanged();
        return JoinResult.Ok();
    }

    public void LeaveRoom(string code, string alias)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(code, out var room)) return;
            room.Participants.RemoveAll(p => p.Alias == alias);
            if (room.Participants.Count == 0)
                _rooms.Remove(code);   // empty room deletes itself
        }
        RaiseChanged();
    }

    // ---------- helpers ----------

    private static RoomInfo Snapshot(Room r) => new(
        r.Code, r.HostAlias, r.Phase, r.MaxPlayers,
        r.Participants.Select(p => new ParticipantInfo(p.Alias, p.Kind, p.Team)).ToList());

    private string GenerateCodeLocked()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // no I/L/O/0/1 — codes stay readable
        while (true)
        {
            var code = new string(Enumerable.Range(0, 4)
                .Select(_ => alphabet[Random.Shared.Next(alphabet.Length)])
                .ToArray());
            if (!_rooms.ContainsKey(code)) return code;
        }
    }

    private void RaiseChanged() => RoomsChanged?.Invoke();
}
