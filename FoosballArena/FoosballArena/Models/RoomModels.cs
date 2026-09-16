namespace FoosballArena.Models;

public enum RoomPhase { Waiting, Active, Finished }
public enum Team { None, A, B }
public enum ParticipantKind { Player, Spectator }

/// <summary>A person inside a room. Lives inside a Room's list (server-side, mutable).</summary>
public class RoomParticipant
{
    public RoomParticipant(string alias, ParticipantKind kind, Team team)
    {
        Alias = alias; Kind = kind; Team = team;
    }

    public string Alias { get; }
    public ParticipantKind Kind { get; }
    public Team Team { get; }
}

/// <summary>One table = one room. All mutation happens inside RoomManager's lock.</summary>
public class Room
{
    public Room(string code, string hostAlias)
    {
        Code = code;
        HostAlias = hostAlias;
        CreatedAt = DateTime.UtcNow;
    }

    public string Code { get; }
    public string HostAlias { get; }
    public DateTime CreatedAt { get; }
    public RoomPhase Phase { get; set; } = RoomPhase.Waiting;
    public int MaxPlayers { get; set; } = 40;
    public List<RoomParticipant> Participants { get; } = new();

    public int PlayerCount => Participants.Count(p => p.Kind == ParticipantKind.Player);
    public int SpectatorCount => Participants.Count(p => p.Kind == ParticipantKind.Spectator);
    public int CountTeam(Team team) => Participants.Count(p => p.Kind == ParticipantKind.Player && p.Team == team);
}

// ---- immutable snapshots: what the UI is allowed to read ----

public sealed record ParticipantInfo(string Alias, ParticipantKind Kind, Team Team);

public sealed record RoomInfo(
    string Code, string HostAlias, RoomPhase Phase, int MaxPlayers,
    IReadOnlyList<ParticipantInfo> Participants)
{
    public int PlayerCount => Participants.Count(p => p.Kind == ParticipantKind.Player);
    public int SpectatorCount => Participants.Count(p => p.Kind == ParticipantKind.Spectator);
}

public sealed record RoomSummary(
    string Code, string HostAlias, RoomPhase Phase, int PlayerCount, int MaxPlayers, int SpectatorCount);

public readonly record struct JoinResult(bool Success, string? Error)
{
    public static JoinResult Ok() => new(true, null);
    public static JoinResult Fail(string error) => new(false, error);
}
