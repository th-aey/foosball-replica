namespace FoosballArena.Services;

using FoosballArena.Models;

public static class Formation
{
    /// <summary>
    /// Which line the Nth player of a team occupies.
    /// 1=DEF, 2=MID, 3=STR (minimum team — no goalkeeper, defender is the last line),
    /// 4=GK (added as people join), then cycle DEF, MID, STR from the 5th.
    /// </summary>
    public static LineRole RoleFor(int teamPosition) => teamPosition switch
    {
        1 => LineRole.Defender,
        2 => LineRole.Midfielder,
        3 => LineRole.Striker,
        4 => LineRole.Goalkeeper,
        var n => ((n - 5) % 3) switch
        {
            0 => LineRole.Defender,
            1 => LineRole.Midfielder,
            _ => LineRole.Striker,
        },
    };

    /// <summary>
    /// Builds slots for every Player. Each line's full-width span is tiled into
    /// equal segments — 1 player covers 0..100, 2 players cover 0..50 and 50..100, etc.
    /// Participants must be passed in join order for stable slot numbering.
    /// </summary>
    public static Dictionary<string, PlayerSlot> Assign(IEnumerable<RoomParticipant> participants)
    {
        var ordered = participants.Where(p => p.Kind == ParticipantKind.Player).ToList();

        var position = new Dictionary<Team, int>();
        var slots = new List<PlayerSlot>();

        foreach (var p in ordered)
        {
            var n = position.TryGetValue(p.Team, out var c) ? c + 1 : 1;
            position[p.Team] = n;
            slots.Add(new PlayerSlot(p.Token, p.Team, RoleFor(n)));
        }

        // GroupBy on a list preserves source order inside each group → slot 0 is always the earliest joiner
        foreach (var line in slots.GroupBy(s => (s.Team, s.Line)))
        {
            var count = line.Count();
            var i = 0;
            foreach (var s in line)
            {
                s.ZoneMin = Pitch.Width * i / count;
                s.ZoneMax = Pitch.Width * (i + 1) / count;
                s.HomeX = (s.ZoneMin + s.ZoneMax) / 2f;
                s.X = s.HomeX;
                s.MoveDir = 0;
                i++;
            }
        }

        return slots.ToDictionary(s => s.Token);
    }
}
