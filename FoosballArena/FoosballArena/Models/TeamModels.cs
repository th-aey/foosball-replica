namespace FoosballArena.Models;

// One club = immutable value object. Record = free equality, no mutation bugs.
public sealed record TeamDef(int Id, string Name, string Primary, string Secondary)
{
    public string Initial => Name.Length > 0 ? Name[..1].ToUpper() : "?";
}

// The global shelf: every room picks from THIS list. No cross-room locking —
// two rooms may both field the Spinners; only *within* a room the sides differ.
public static class TeamCatalog
{
    public static readonly IReadOnlyList<TeamDef> All = new[]
    {
        new TeamDef(1, "Soweto Spinners",   "#f0b429", "#111111"),
        new TeamDef(2, "Cape Town Kickoff", "#38bdf8", "#f8fafc"),
        new TeamDef(3, "Durban Marlins",    "#14b8a6", "#0f2f3f"),
        new TeamDef(4, "Pretoria Pacers",   "#a78bfa", "#f8fafc"),
        new TeamDef(5, "Gqeberha Gales",    "#4ade80", "#14532d"),
        new TeamDef(6, "Bloem Braves",      "#ef4444", "#7f1d1d"),
        new TeamDef(7, "Kimberley Kings",   "#e879f9", "#4a044e"),
        new TeamDef(8, "Polokwane Pride",   "#a3e635", "#1a2e05"),
    };

    public static TeamDef GetOrFirst(int id) =>
        All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
