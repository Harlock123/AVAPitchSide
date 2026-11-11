namespace AVAPitchSide.Engine;

/// <summary>A strip: shirt (with an optional second colour in stripes), shorts and socks.</summary>
public sealed record Kit(uint Shirt, uint Second, uint Shorts, uint Socks, bool Stripes = false);

/// <summary>A team: its name, how good it is (0 to 1), its two strips, the keeper's shirt and its eleven players.</summary>
public sealed record Team(string Name, string Short, double Skill, Kit Home, Kit Away, uint Keeper, IReadOnlyList<string> Players);

/// <summary>Eight made-up clubs from made-up towns, with made-up players.</summary>
public static class League
{
    private static readonly string[] Starts = ["BRAN", "CAL", "DOR", "FEN", "GAR", "HAL", "KER", "LAN", "MOR", "NAS", "PEL", "RAD", "SOL", "TOR", "VAN", "WEL", "ORM", "EST", "ILV", "ANS", "QUIL", "BRE", "COR", "DAV"];
    private static readonly string[] Ends = ["ROW", "DEN", "ICK", "OLT", "ANE", "ISH", "ETT", "ARD", "ELL", "ONO", "AKI", "ERS", "URN", "AVA", "IEL", "OSS", "UND", "EGO"];

    /// <summary>Eleven surnames from syllables, the same every time for a team.</summary>
    private static string[] Squad(int seed)
    {
        var r = new Random(seed * 7919 + 13);
        var names = new List<string>();
        while (names.Count < 11)
        {
            var n = Starts[r.Next(Starts.Length)] + Ends[r.Next(Ends.Length)];
            if (!names.Contains(n)) names.Add(n);
        }
        return [.. names];
    }

    private static Team T(int i, string name, string shortName, double skill, Kit home, Kit away, uint keeper) =>
        new(name, shortName, skill, home, away, keeper, Squad(i));

    public static readonly IReadOnlyList<Team> Teams =
    [
        T(0, "BRASSWICK", "BRA", 0.80, new(0xFFD8282A, 0xFFF4F4F4, 0xFFF4F4F4, 0xFFD8282A), new(0xFF1A1A1A, 0xFFD8282A, 0xFF1A1A1A, 0xFF1A1A1A), 0xFF2AC85A),
        T(1, "CALDERHOLT", "CAL", 0.72, new(0xFF2A5AD8, 0xFF2A5AD8, 0xFFF4F4F4, 0xFF2A5AD8), new(0xFFF0D030, 0xFF2A5AD8, 0xFF2A5AD8, 0xFFF0D030), 0xFFE86A1A),
        T(2, "PORT ESKAR", "POR", 0.64, new(0xFFF08A1A, 0xFF1A1A1A, 0xFF1A1A1A, 0xFFF08A1A), new(0xFFF4F4F4, 0xFFF08A1A, 0xFFF4F4F4, 0xFFF4F4F4), 0xFF3A3AD0),
        T(3, "MERROWDALE", "MER", 0.58, new(0xFF1A8A4A, 0xFFF4F4F4, 0xFFF4F4F4, 0xFF1A8A4A, Stripes: true), new(0xFF6A2A8A, 0xFF6A2A8A, 0xFF6A2A8A, 0xFFF4F4F4), 0xFFE8E030),
        T(4, "GLENVASKY", "GLE", 0.50, new(0xFF7A1A3A, 0xFF8AC8F0, 0xFFF4F4F4, 0xFF8AC8F0), new(0xFF8AC8F0, 0xFF7A1A3A, 0xFF7A1A3A, 0xFF8AC8F0), 0xFF1A1A1A),
        T(5, "ORMSHAW", "ORM", 0.44, new(0xFFF4F4F4, 0xFFF4F4F4, 0xFF1A2A5A, 0xFFF4F4F4), new(0xFFD83A3A, 0xFFD83A3A, 0xFFD83A3A, 0xFFD83A3A), 0xFF2A8A8A),
        T(6, "TALLISTON", "TAL", 0.38, new(0xFF1A1A1A, 0xFFF0D030, 0xFF1A1A1A, 0xFFF0D030, Stripes: true), new(0xFFF4F4F4, 0xFF1A1A1A, 0xFF1A1A1A, 0xFFF4F4F4), 0xFFD83A8A),
        T(7, "WRENFORD", "WRE", 0.32, new(0xFF1AA8A8, 0xFF1AA8A8, 0xFF1AA8A8, 0xFFF4F4F4), new(0xFFF0A0B0, 0xFFF0A0B0, 0xFF1A1A1A, 0xFF1A1A1A), 0xFFF08A1A),
    ];

    private static double Distance(uint a, uint b)
    {
        double d = 0;
        for (int s = 0; s <= 16; s += 8) d += Math.Abs((int)((a >> s) & 0xFF) - (int)((b >> s) & 0xFF));
        return d;
    }

    /// <summary>The away side wears its second strip if the first is too like the home side's.</summary>
    public static Kit AwayKit(Team home, Team away) =>
        Distance(home.Home.Shirt, away.Home.Shirt) + Distance(home.Home.Shorts, away.Home.Shorts) * 0.4 < 240 ? away.Away : away.Home;
}
