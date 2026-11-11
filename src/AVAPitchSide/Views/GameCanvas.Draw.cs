using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAPitchSide.Engine;

namespace AVAPitchSide.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAPitchSide/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Shade(uint c, double k)
    {
        byte r = (byte)Math.Clamp(((c >> 16) & 0xFF) * k, 0, 255), g = (byte)Math.Clamp(((c >> 8) & 0xFF) * k, 0, 255), b = (byte)Math.Clamp((c & 0xFF) * k, 0, 255);
        return 0xFF000000 | (uint)r << 16 | (uint)g << 8 | b;
    }

    private static uint Alpha(uint c, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (c & 0xFFFFFF);

    private static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int s) => (uint)(((a >> s) & 0xFF) + ((((b >> s) & 0xFF) - (double)((a >> s) & 0xFF)) * t));
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    // ───────────────────────────── the pitch ─────────────────────────────

    /// <summary>How big the pitch is drawn: a little over one pixel to a pitch unit.</summary>
    public const double Zoom = 1.5;
    private double _camX, _camY, _camClock = -1;
    /// <summary>How far up the screen a ball (or a crossbar) at height z is drawn.</summary>
    private const double Lift = 0.8;

    private static double ViewW => 1024 / Zoom;
    private static double ViewH => 768 / Zoom;
    /// <summary>A pitch point on the screen (inside the zoomed transform).</summary>
    private Point S(Vec v, double z = 0) => new(v.X - _camX, v.Y - _camY - z * Lift);
    private Point S(double x, double y, double z = 0) => new(x - _camX, y - _camY - z * Lift);

    /// <summary>The view eases toward the ball by the clock, so it moves the same however often the screen is drawn.</summary>
    private void FollowCamera(Vec ball, double rate)
    {
        double dt = _clock - _camClock;
        _camClock = _clock;
        double k = dt is < 0 or > 1.5 ? 1 : 1 - Math.Exp(-dt * rate);
        _camX += (ball.X - ViewW * 0.5 - _camX) * k;
        _camY += (ball.Y - ViewH * 0.5 - _camY) * k;
        ClampCamera();
    }

    private void ClampCamera()
    {
        _camX = Math.Clamp(_camX, -Pitch.Margin - 64, Pitch.Width + Pitch.Margin + 64 - ViewW);
        _camY = Math.Clamp(_camY, -Pitch.Margin - 70, Pitch.Length + Pitch.Margin + 70 - ViewH);
    }

    private void DrawPitch(DrawingContext dc)
    {
        double top = _camY, bottom = _camY + ViewH;
        // The grass, mown in stripes.
        dc.FillRectangle(Brush(0xFF2A6A2A), new Rect(0, 0, ViewW, ViewH));
        for (int i = (int)Math.Floor((top + Pitch.Margin) / 70) - 1; i * 70 - Pitch.Margin < bottom; i++)
        {
            double y = i * 70 - Pitch.Margin;
            dc.FillRectangle(Brush(i % 2 == 0 ? 0xFF3A8A34 : 0xFF348030), new Rect(S(-Pitch.Margin, y).X, S(0, y).Y, Pitch.Width + 2 * Pitch.Margin, 71));
        }

        DrawStands(dc);

        var line = Pen(0xE8F4F8F0, 2.2);
        dc.DrawRectangle(null, line, new Rect(S(0, 0), S(Pitch.Width, Pitch.Length)));
        dc.DrawLine(line, S(0, Pitch.Length / 2), S(Pitch.Width, Pitch.Length / 2));
        dc.DrawEllipse(null, line, S(Pitch.Centre), Pitch.CentreCircle, Pitch.CentreCircle);
        dc.DrawEllipse(Brush(0xE8F4F8F0), null, S(Pitch.Centre), 3, 3);
        foreach (double gy in new[] { 0.0, Pitch.Length })
        {
            double s = gy == 0 ? 1 : -1;
            dc.DrawRectangle(null, line, new Rect(S(Pitch.Width / 2 - Pitch.BoxHalf, gy), S(Pitch.Width / 2 + Pitch.BoxHalf, gy + s * Pitch.BoxDepth)).Normalize());
            dc.DrawRectangle(null, line, new Rect(S(Pitch.Width / 2 - Pitch.SixHalf, gy), S(Pitch.Width / 2 + Pitch.SixHalf, gy + s * Pitch.SixDepth)).Normalize());
            var spot = new Vec(Pitch.Width / 2, gy + s * Pitch.PenaltySpot);
            dc.DrawEllipse(Brush(0xE8F4F8F0), null, S(spot), 2.6, 2.6);
            // The arc at the edge of the area: the part of the circle round the spot outside the box.
            double r = 110, dy = Pitch.BoxDepth - Pitch.PenaltySpot, half = Math.Acos(dy / r);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                double a0 = Math.PI / 2 - half, a1 = Math.PI / 2 + half;
                Point At(double a) => S(spot.X + r * Math.Cos(a), spot.Y + s * r * Math.Sin(a));
                c.BeginFigure(At(a0), false);
                c.ArcTo(At(a1), new Size(r, r), 0, false, s > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                c.EndFigure(false);
            }
            dc.DrawGeometry(null, line, g);
            foreach (double cx in new[] { 0.0, Pitch.Width })
            {
                var corner = new Vec(cx, gy);
                double sx = cx == 0 ? 1 : -1;
                var arc = new StreamGeometry();
                using (var c = arc.Open())
                {
                    c.BeginFigure(S(cx + sx * 12, gy), false);
                    c.ArcTo(S(cx, gy + s * 12), new Size(12, 12), 0, false, sx * s > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                    c.EndFigure(false);
                }
                dc.DrawGeometry(null, line, arc);
                // The flag.
                dc.DrawLine(Pen(0xFFE8E8E0, 1.5), S(corner), S(corner, 24));
                dc.FillRectangle(Brush(0xFFFFD030), new Rect(S(corner, 24), new Size(7, 5)));
            }
        }
        DrawGoal(dc, 0);
    }

    /// <summary>A goal: the net behind the line, posts and the crossbar, raised by their height.</summary>
    private void DrawGoal(DrawingContext dc, double gy)
    {
        double s = gy == 0 ? -1 : 1, l = Pitch.Width / 2 - Pitch.GoalHalf, r = Pitch.Width / 2 + Pitch.GoalHalf;
        var net = Pen(0x90FFFFFF, 1);
        double back = gy + s * Pitch.NetDepth;
        // The net's roof and back, as a mesh.
        Poly(dc, 0x30FFFFFF, null, 0, S(l, gy, Pitch.Crossbar), S(r, gy, Pitch.Crossbar), S(r, back, Pitch.Crossbar * 0.7), S(l, back, Pitch.Crossbar * 0.7));
        for (double x = l; x <= r + 0.1; x += 8) dc.DrawLine(net, S(x, gy, Pitch.Crossbar), S(x, back, Pitch.Crossbar * 0.7));
        for (double x = l; x <= r + 0.1; x += 8) dc.DrawLine(net, S(x, back, Pitch.Crossbar * 0.7), S(x, back, 0));
        for (double h = 0; h <= Pitch.Crossbar * 0.7; h += 6) dc.DrawLine(net, S(l, back, h), S(r, back, h));
        dc.DrawLine(net, S(l, back, 0), S(l, gy, 0));
        dc.DrawLine(net, S(r, back, 0), S(r, gy, 0));
        var post = Pen(0xFFFFFFFF, 3);
        dc.DrawLine(post, S(l, gy), S(l, gy, Pitch.Crossbar));
        dc.DrawLine(post, S(r, gy), S(r, gy, Pitch.Crossbar));
        dc.DrawLine(post, S(l, gy, Pitch.Crossbar), S(r, gy, Pitch.Crossbar));
    }

    private void DrawStands(DrawingContext dc)
    {
        // Advertising boards round the pitch, and the crowd beyond them.
        double m = Pitch.Margin;
        var boards = new[] { "AVA", "PITCH SIDE", "KICK OFF", "GOAL!", "AVA", "MATCH DAY" };
        foreach (double y in new[] { -m, Pitch.Length + m - 12 })
        {
            dc.FillRectangle(Brush(0xFF1A2A4A), new Rect(S(-m, y), new Size(Pitch.Width + 2 * m, 12)));
            for (int i = 0; i < 6; i++) Text(dc, boards[i], new Point(S(-m + 40 + i * 160, y + 6).X, S(0, y + 6).Y), 13, 0xFFF0D040, shadow: false);
        }
        for (int band = 0; band < 2; band++)
        {
            double y0 = band == 0 ? -m - 130 : Pitch.Length + m, y1 = y0 + 130;
            if (S(0, y1).Y < 0 || S(0, y0).Y > ViewH) continue;
            dc.FillRectangle(Brush(0xFF1A1A22), new Rect(S(-m - 60, y0), S(Pitch.Width + m + 60, y1)));
            for (int row = 0; row < 10; row++)
                for (int k = 0; k < 64; k++)
                {
                    uint h = Hash(k, row + band * 20, 3);
                    double bob = _cheer > 0 ? Math.Abs(Math.Sin(_clock * 12 + k + row)) * 3 : 0;
                    var at = S(-m - 50 + k * 15 + row % 2 * 7, y0 + 8 + row * 12 - bob);
                    dc.FillRectangle(Brush(Crowd[h % (uint)Crowd.Length]), new Rect(at, new Size(6, 7)));
                }
        }
        // Down the sides, a strip of crowd.
        foreach (double x in new[] { -m - 60.0, Pitch.Width + m + 4 })
            for (int k = 0; k < 120; k++)
            {
                double y = -m + k * 13;
                if (S(0, y).Y < -20 || S(0, y).Y > ViewH + 20) continue;
                for (int col = 0; col < 3; col++)
                    dc.FillRectangle(Brush(Crowd[Hash(k, col, (int)x) % (uint)Crowd.Length]), new Rect(S(x + col * 18, y), new Size(7, 6)));
            }
    }

    private static readonly uint[] Crowd = [0xFFD84A3A, 0xFF3A6AD8, 0xFFE8E0D0, 0xFFE8C030, 0xFF2A2A2A, 0xFF8A4A2A, 0xFF4A8A4A, 0xFFD8A080];
    private double _cheer;

    // ───────────────────────────── players and the ball ─────────────────────────────

    private void DrawPlayer(DrawingContext dc, Match m, Player p, Vec pos, Vec facing, PState state, double anim, uint? marker)
    {
        var kit = m.Kits[p.Side];
        uint shirt = p.IsKeeper ? m.Teams[p.Side].Keeper : kit.Shirt, second = p.IsKeeper ? Shade(shirt, 0.7) : kit.Second;
        uint shorts = p.IsKeeper ? 0xFF1A1A1A : kit.Shorts, socks = p.IsKeeper ? shirt : kit.Socks;
        uint skin = SkinOf(p), hair = HairOf(p), ink = 0xFF101010;
        var f = S(pos);
        dc.DrawEllipse(Brush(0x50000000), null, new Point(f.X + 2, f.Y + 1), 7, 3);

        if (state is PState.Slide or PState.Down or PState.Dive)
        {
            // Lying along the grass: feet first in a slide, head first in a dive.
            var dir = facing == Vec.Zero ? new Vec(1, 0) : facing;
            double lift = state == PState.Dive ? 6 : 0;
            var feet = state == PState.Dive ? pos - dir * 9 : pos + dir * 6;
            var top = state == PState.Dive ? pos + dir * 7 : pos - dir * 9;
            var hip = feet + (top - feet) * 0.4;
            dc.DrawLine(Pen(ink, 7), S(feet, lift), S(top, lift));
            dc.DrawLine(Pen(socks, 4), S(feet, lift), S(hip, lift));
            dc.DrawLine(Pen(shorts, 5), S(hip, lift), S(hip + (top - feet) * 0.15, lift));
            dc.DrawLine(Pen(shirt, 6), S(hip + (top - feet) * 0.15, lift), S(top - (top - feet) * 0.12, lift));
            if (state == PState.Dive) dc.DrawLine(Pen(skin, 2.5), S(top, lift), S(top + dir * 6, lift));
            dc.DrawEllipse(Brush(hair), Pen(ink, 1), S(top, lift), 3.4, 3.4);
            return;
        }

        double swing = Math.Sin(anim) * 2.4;
        bool front = facing.Y > 0.35, back = facing.Y < -0.35;
        double turn = facing.X * 1.2;
        // Legs and socks, swinging.
        dc.FillRectangle(Brush(skin), new Rect(f.X - 3.6, f.Y - 8 + swing * 0.5, 2.6, 4));
        dc.FillRectangle(Brush(skin), new Rect(f.X + 1, f.Y - 8 - swing * 0.5, 2.6, 4));
        dc.FillRectangle(Brush(socks), new Rect(f.X - 3.6, f.Y - 4.5 + swing, 2.6, 4.5));
        dc.FillRectangle(Brush(socks), new Rect(f.X + 1, f.Y - 4.5 - swing, 2.6, 4.5));
        dc.FillRectangle(Brush(ink), new Rect(f.X - 3.8, f.Y - 0.6 + swing, 3, 1.6));
        dc.FillRectangle(Brush(ink), new Rect(f.X + 0.8, f.Y - 0.6 - swing, 3, 1.6));
        // Shorts and shirt.
        dc.FillRectangle(Brush(shorts), new Rect(f.X - 4.4, f.Y - 11, 8.8, 4));
        bool arms = state == PState.Celebrate || state == PState.Hold;
        double armY = arms ? -22 : -16;
        dc.DrawLine(Pen(shirt, 2.6), new Point(f.X - 5, f.Y - 16), new Point(f.X - 6.5, f.Y + armY + 6 - swing * (arms ? 0 : 1)));
        dc.DrawLine(Pen(shirt, 2.6), new Point(f.X + 5, f.Y - 16), new Point(f.X + 6.5, f.Y + armY + 6 + swing * (arms ? 0 : 1)));
        dc.FillRectangle(Brush(shirt), new Rect(f.X - 5, f.Y - 18.5, 10, 8.5), 2);
        if (kit.Stripes && !p.IsKeeper)
        {
            dc.FillRectangle(Brush(second), new Rect(f.X - 2.8, f.Y - 18.5, 1.8, 8.5));
            dc.FillRectangle(Brush(second), new Rect(f.X + 1, f.Y - 18.5, 1.8, 8.5));
        }
        else dc.FillRectangle(Brush(second), new Rect(f.X - 5, f.Y - 18.5, 10, 1.6));
        if (back) Text(dc, p.Number.ToString(), new Point(f.X, f.Y - 14.5), 10, second == shirt ? 0xFFFFFFFF : second, center: true, shadow: false);
        // Head: the face when he comes toward us, the back of the head going away.
        var head = new Point(f.X + turn, f.Y - 22);
        dc.DrawEllipse(Brush(back ? hair : skin), Pen(ink, 0.8), head, 3.6, 3.6);
        if (!back)
        {
            dc.DrawEllipse(Brush(hair), null, new Point(head.X - turn * 0.6, head.Y - 1.6), 3.3, front ? 2 : 2.6);
            if (front) dc.FillRectangle(Brush(ink), new Rect(head.X - 1.6, head.Y + 0.2, 0.9, 0.9));
            if (front) dc.FillRectangle(Brush(ink), new Rect(head.X + 0.8, head.Y + 0.2, 0.9, 0.9));
        }

        if (marker is { } mk)
        {
            var tip = new Point(f.X, f.Y - 29);
            Poly(dc, mk, 0xFF000000, 1, tip, new Point(tip.X - 4, tip.Y - 6), new Point(tip.X + 4, tip.Y - 6));
        }
    }

    private static uint SkinOf(Player p) => (Hash(p.Index, p.Side, 41) % 5) switch { 0 => 0xFF5A3A28, 1 => 0xFF8A5A3A, 2 => 0xFFC89070, _ => 0xFFF0C8A8 };
    private static uint HairOf(Player p) => (Hash(p.Index, p.Side, 43) % 4) switch { 0 => 0xFF1A1410, 1 => 0xFF4A2A14, 2 => 0xFFC8A050, _ => 0xFF2A2A2A };

    private void DrawBall(DrawingContext dc, Vec pos, double z)
    {
        double r = 3.6 * (1 + z / 140);
        dc.DrawEllipse(Brush(Alpha(0xFF000000, Math.Max(0.15, 0.45 - z / 120))), null, S(pos) + new Point(1 + z * 0.15, 0.5), 3.4 + z / 60, 2);
        var at = S(pos, z);
        dc.DrawEllipse(Brush(0xFFFAFAFA), Pen(0xFF202020, 1), at, r, r);
        double spin = _clock * 9;
        dc.DrawEllipse(Brush(0xFF202020), null, new Point(at.X + Math.Cos(spin) * r * 0.35, at.Y + Math.Sin(spin) * r * 0.3), r * 0.35, r * 0.35);
    }
}
