namespace FoosballArena.Models;

public enum LineRole { Goalkeeper, Defender, Midfielder, Striker }

/// <summary>
/// Pitch coordinates: X = 0..100 across the width, Y = 0..200 goal to goal.
/// Team A defends Y=0, Team B defends Y=200.
/// Width is exactly 100 so zone edges double as CSS percentages.
/// </summary>
public static class Pitch
{
    public const float Width = 100f;
    public const float Length = 200f;
    public const float PlayerSpeed = 35f; // lateral units per second

    public static float LineY(Team team, LineRole role) => team switch
    {
        Team.A => role switch
        {
            LineRole.Goalkeeper => 10f,
            LineRole.Defender => 50f,
            LineRole.Midfielder => 90f,
            LineRole.Striker => 130f,
            _ => 0f,
        },
        Team.B => role switch
        {
            LineRole.Goalkeeper => 190f,
            LineRole.Defender => 150f,
            LineRole.Midfielder => 110f,
            LineRole.Striker => 70f,
            _ => 0f,
        },
        _ => 0f,
    };
}

/// <summary>A player's assigned place on the pitch. Lives in Room.Slots (mutated under RoomManager's lock only).</summary>
public class PlayerSlot
{
    public PlayerSlot(string token, Team team, LineRole line)
    {
        Token = token;
        Team = team;
        Line = line;
        LineY = Pitch.LineY(team, line);
    }

    public string Token { get; }
    public Team Team { get; }
    public LineRole Line { get; }
    public float LineY { get; }

    public float ZoneMin { get; set; }     // left edge of my movement range
    public float ZoneMax { get; set; }     // right edge
    public float HomeX { get; set; }       // zone centre — where I line up
    public float X { get; set; }           // current lateral position
    public double MoveDir { get; set; }    // -1..1 from input; game loop integrates this
    public float LastRaisedX { get; set; } // event-throttle bookkeeping
}

public sealed record SlotInfo(string Token, string Alias, Team Team, LineRole Line,
                              float ZoneMin, float ZoneMax, float X);