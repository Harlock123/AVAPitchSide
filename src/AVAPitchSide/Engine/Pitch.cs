namespace AVAPitchSide.Engine;

/// <summary>A point or a direction on the pitch, in pitch units (12 to the metre); y grows down the screen.</summary>
public readonly record struct Vec(double X, double Y)
{
    public static readonly Vec Zero = new(0, 0);
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator -(Vec a) => new(-a.X, -a.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public static Vec operator *(double k, Vec a) => new(a.X * k, a.Y * k);
    public static Vec operator /(Vec a, double k) => new(a.X / k, a.Y / k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public Vec Norm => Length < 1e-9 ? Zero : this / Length;
    public double Dot(Vec b) => X * b.X + Y * b.Y;
    /// <summary>A quarter turn of this direction: sideways to it.</summary>
    public Vec Perp => new(-Y, X);
    public double DistanceTo(Vec b) => (this - b).Length;
    public Vec Clamp(double max) => Length > max ? Norm * max : this;

    // The record's own ToString would print Norm and Perp, which are Vecs too, for ever.
    public override string ToString() => $"({X:0.#}, {Y:0.#})";
}

/// <summary>
/// The pitch's measurements, in pitch units of 12 to the metre: 68 m by 105 m, the goal lines across the top
/// (y = 0) and the bottom (y = <see cref="Length"/>), and a margin of grass and stands around it.
/// </summary>
public static class Pitch
{
    public const double Width = 816, Length = 1260, Margin = 70;
    public const double GoalHalf = 44, Crossbar = 29, NetDepth = 26;
    public const double BoxHalf = 242, BoxDepth = 198, SixHalf = 110, SixDepth = 66, PenaltySpot = 132;
    public const double CentreCircle = 110;
    public const double Gravity = 420;

    public static Vec Centre => new(Width / 2, Length / 2);

    /// <summary>Inside the penalty area at the goal on line <paramref name="goalY"/> (0 or <see cref="Length"/>).</summary>
    public static bool InBox(Vec p, double goalY, double slack = 0) =>
        Math.Abs(p.X - Width / 2) < BoxHalf + slack && Math.Abs(p.Y - goalY) < BoxDepth + slack;

    public static bool InPlay(Vec p) => p.X >= 0 && p.X <= Width && p.Y >= 0 && p.Y <= Length;
}
