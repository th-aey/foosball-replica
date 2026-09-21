namespace FoosballArena.Models;

public enum RoomPhase { Waiting, Countdown, Active, GoalReset, Finished }
public enum Team { None, A, B }
public enum ParticipantKind { Player, Spectator }

public class RoomParticipant
{
    public RoomParticipant(string token, string alias, ParticipantKind kind, Team team)
    {
        Token = token; Alias = alias; Kind = kind; Team = team;
        LastSeen = DateTime.UtcNow;
    }

    public string Token { get; }              // unique identity for this participant
    public string Alias { get; }
    public ParticipantKind Kind { get; set; } // settable: queued spectators get promoted
    public Team Team { get; set; }            // settable: rebalanced on promotion
    public DateTime LastSeen { get; set; }    // presence: updated by heartbeats
}

public class Room
{
    public Room(string code, string hostAlias, string hostToken)
    {
        Code = code; HostAlias = hostAlias; HostToken = hostToken;
        CreatedAt = DateTime.UtcNow;
    }

    public string Code { get; }
    public string HostAlias { get; }
    public string HostToken { get; }
    public DateTime CreatedAt { get; }

    public RoomPhase Phase { get; set; } = RoomPhase.Waiting;
    public int MaxPlayers { get; set; } = 40;

    public int ScoreA { get; set; }
    public int ScoreB { get; set; }

    // PhaseEndsAt drives countdown / goal-reset / intermission deadlines.
    // MatchEndsAt drives the 5-minute clock — it must SURVIVE goal resets,
    // which is why we keep two separate deadlines.
    public DateTime PhaseEndsAt { get; set; }
    public DateTime MatchEndsAt { get; set; }

    public int LastShownSecond { get; set; } = -1; // event throttle, managed by TickAll

    public List<RoomParticipant> Participants { get; } = new();
    public Queue<string> PlayerQueue { get; } = new(); // tokens queued for next match

    /// <summary>Formation slots per token. Mutated only by RoomManager under its lock.</summary>
    public Dictionary<string, PlayerSlot> Slots { get; } = new();

    public int PlayerCount => Participants.Count(p => p.Kind == ParticipantKind.Player);
    public int SpectatorCount => Participants.Count(p => p.Kind == ParticipantKind.Spectator);
    public int CountTeam(Team team) => Participants.Count(p => p.Kind == ParticipantKind.Player && p.Team == team);
}

// ---------- UI snapshots ----------

public sealed record ParticipantInfo(string Alias, ParticipantKind Kind, Team Team, bool Queued);

public sealed record RoomInfo(
    string Code, string HostAlias, string HostToken, RoomPhase Phase,
    int MaxPlayers, int ScoreA, int ScoreB, int SecondsLeft,
    IReadOnlyList<ParticipantInfo> Participants, int QueuedCount,
    IReadOnlyList<SlotInfo> Slots)
{
    public int PlayerCount => Participants.Count(p => p.Kind == ParticipantKind.Player);
    public int SpectatorCount => Participants.Count(p => p.Kind == ParticipantKind.Spectator);
}

public sealed record RoomSummary(
    string Code, string HostAlias, RoomPhase Phase,
    int PlayerCount, int MaxPlayers, int SpectatorCount, int ScoreA, int ScoreB);

public readonly record struct JoinResult(bool Success, string? Error, string Token)
{
    public static JoinResult Ok(string token) => new(true, null, token);
    public static JoinResult Fail(string error) => new(false, error, "");
}
