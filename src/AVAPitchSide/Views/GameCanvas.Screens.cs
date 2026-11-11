using Avalonia;
using Avalonia.Media;
using AVAPitchSide.Engine;

namespace AVAPitchSide.Views;

public sealed partial class GameCanvas
{
    private const double W = 1024, H = 768;
    private const uint P1Colour = 0xFF5AE8FF, P2Colour = 0xFFFFA03A;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawBackdrop(dc); DrawTitle(dc); break;
            case GameState.Select: DrawBackdrop(dc); DrawSelect(dc); break;
            case GameState.HighScores: DrawBackdrop(dc); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawBackdrop(dc); DrawInitials(dc); break;
            case GameState.GameOver: DrawMatch(dc); DrawGameOver(dc); break;
            default:
                DrawMatch(dc);
                switch (g.Stage)
                {
                    case Stage.Intro: DrawIntro(dc); break;
                    case Stage.HalfTime: DrawResultCard(dc, "HALF TIME"); break;
                    case Stage.FullTime: DrawResultCard(dc, "FULL TIME"); break;
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xC0000000), new Rect(0, 0, W, H));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract && (int)(_clock * 2) % 2 == 0)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 740), 26, Yellow, center: true);
    }

    private static void Card(DrawingContext dc, Rect r)
    {
        dc.FillRectangle(Brush(0xE8081410), r, 8);
        dc.DrawRectangle(null, Pen(0xFFE8E0C0, 3), r, 8, 8);
        dc.DrawRectangle(null, Pen(0xFF3A5A3A, 2), r.Deflate(6), 5, 5);
    }

    // ───────────────────────────── the match ─────────────────────────────

    private void DrawMatch(DrawingContext dc)
    {
        var g = Engine;
        if (g.Match is not { } m) return;
        var frame = g.Stage == Stage.Replay ? g.ReplayFrame : null;
        var ball = frame?.Ball ?? m.Ball.Pos;
        double ballZ = frame?.BallZ ?? m.Ball.Z;
        FollowCamera(ball, frame != null ? 9 : 6);
        _cheer = m.Phase == MatchPhase.Goal ? 1 : Math.Max(0, _cheer - 0.01);

        using (dc.PushTransform(Matrix.CreateScale(Zoom, Zoom)))
        {
            DrawPitch(dc);
            // Back to front, so nearer players stand in front of farther ones.
            var order = Enumerable.Range(0, m.Players.Count).OrderBy(i => frame?.Pos[i].Y ?? m.Players[i].Pos.Y).ToList();
            bool ballDrawn = false;
            foreach (int i in order)
            {
                var p = m.Players[i];
                var pos = frame?.Pos[i] ?? p.Pos;
                if (!ballDrawn && ball.Y < pos.Y) { DrawBall(dc, ball, ballZ); ballDrawn = true; }
                uint? marker = frame == null && m.Controlled[0] == p ? P1Colour : frame == null && m.Controlled[1] == p ? P2Colour : null;
                DrawPlayer(dc, m, p, pos, frame?.Facing[i] ?? p.Facing, frame?.State[i] ?? p.State, frame?.Anim[i] ?? p.Anim, marker);
            }
            if (!ballDrawn) DrawBall(dc, ball, ballZ);
            DrawGoal(dc, Pitch.Length);
        }

        DrawHud(dc, m);
        if (frame != null)
        {
            dc.FillRectangle(Brush(0x40000000), new Rect(0, 0, W, H));
            if ((int)(_clock * 2) % 2 == 0) Text(dc, "REPLAY", new Point(60, 110), 40, Red);
        }
    }

    private void DrawHud(DrawingContext dc, Match m)
    {
        var g = Engine;
        // Score and clock.
        dc.FillRectangle(Brush(0xE0081008), new Rect(16, 14, 370, 50), 6);
        Chip(dc, new Point(30, 39), m.Kits[0]);
        Text(dc, m.Teams[0].Short, new Point(48, 39), 34, White);
        Text(dc, $"{m.Score[0]} - {m.Score[1]}", new Point(160, 39), 38, Yellow, center: true);
        Text(dc, m.Teams[1].Short, new Point(212, 39), 34, White);
        Chip(dc, new Point(274, 39), m.Kits[1]);
        int minute = (int)((m.Half - 1) * 45 + Math.Min(45, m.Clock / m.HalfLength * 45));
        Text(dc, m.Shootout ? "PENS" : $"{minute}'", new Point(338, 39), 32, Cyan, center: true);
        if (m.Shootout) DrawPenalties(dc, m);

        if (g.Banner is { } banner && g.Stage == Stage.Play)
        {
            bool goal = banner.StartsWith("GOAL", StringComparison.Ordinal) || banner.StartsWith("OWN", StringComparison.Ordinal);
            Text(dc, goal ? "GOAL!" : banner, new Point(512, 300), goal ? 120 : 46, goal ? ((int)(_clock * 6) % 2 == 0 ? Yellow : White) : White, center: true);
            if (goal) Text(dc, banner.StartsWith("OWN", StringComparison.Ordinal) ? banner : banner[5..].Trim(), new Point(512, 380), 40, White, center: true);
        }

        // Who you're running, by name.
        for (int side = 0; side < 2; side++)
            if (m.Controlled[side] is { } c && g.Stage == Stage.Play)
                Text(dc, $"{c.Number} {c.Name}", new Point(side == 0 ? 20 : 620, 744), 28, side == 0 ? P1Colour : P2Colour);
        DrawRadar(dc, m);
    }

    private static void Chip(DrawingContext dc, Point at, Kit k)
    {
        dc.FillRectangle(Brush(k.Shirt), new Rect(at.X - 8, at.Y - 10, 16, 12), 2);
        dc.FillRectangle(Brush(k.Shorts), new Rect(at.X - 8, at.Y + 2, 16, 7), 1);
        if (k.Stripes) dc.FillRectangle(Brush(k.Second), new Rect(at.X - 2, at.Y - 10, 4, 12));
        dc.DrawRectangle(null, Pen(0xFF000000, 1), new Rect(at.X - 8, at.Y - 10, 16, 19), 2, 2);
    }

    /// <summary>The whole pitch in miniature, every player a dot, and the part on screen outlined.</summary>
    private void DrawRadar(DrawingContext dc, Match m)
    {
        const double rw = 92, rh = 142, x0 = 912, y0 = 610;
        double k = rw / Pitch.Width;
        dc.FillRectangle(Brush(0xA0103010), new Rect(x0 - 6, y0 - 6, rw + 12, rh + 12), 4);
        dc.DrawRectangle(null, Pen(0xC0E8F0E8, 1), new Rect(x0, y0, rw, rh));
        dc.DrawLine(Pen(0xC0E8F0E8, 1), new Point(x0, y0 + rh / 2), new Point(x0 + rw, y0 + rh / 2));
        foreach (var p in m.Players)
            dc.DrawEllipse(Brush(p.IsKeeper ? m.Teams[p.Side].Keeper : m.Kits[p.Side].Shirt), Pen(0xFF000000, 0.6), new Point(x0 + p.Pos.X * k, y0 + p.Pos.Y * k), 2.4, 2.4);
        dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(x0 + m.Ball.Pos.X * k, y0 + m.Ball.Pos.Y * k), 1.8, 1.8);
        var view = new Rect(x0 + _camX * k, y0 + _camY * k, ViewW * k, ViewH * k).Intersect(new Rect(x0, y0, rw, rh));
        dc.DrawRectangle(null, Pen(0x90FFFF60, 1), view);
    }

    private static void DrawPenalties(DrawingContext dc, Match m)
    {
        dc.FillRectangle(Brush(0xE0081008), new Rect(16, 70, 370, 64), 6);
        for (int side = 0; side < 2; side++)
        {
            Text(dc, m.Teams[side].Short, new Point(30, 88 + side * 28), 26, White);
            for (int i = 0; i < Math.Max(5, m.Penalties[side].Count); i++)
            {
                var at = new Point(100 + i * 24, 88 + side * 28);
                uint col = i < m.Penalties[side].Count ? (m.Penalties[side][i] ? 0xFF5AE85A : 0xFFE84A3A) : 0xFF3A4A3A;
                dc.DrawEllipse(Brush(col), Pen(0xFF000000, 1.5), at, 8, 8);
            }
        }
    }

    private void DrawIntro(DrawingContext dc)
    {
        var g = Engine;
        var m = g.Match!;
        dc.FillRectangle(Brush(0x80000000), new Rect(0, 0, W, H));
        Card(dc, new Rect(172, 200, 680, 300));
        Text(dc, g.RoundNamesFor(g.Round), new Point(512, 250), 48, Yellow, center: true);
        Chip(dc, new Point(260, 345), m.Kits[0]);
        Chip(dc, new Point(764, 345), m.Kits[1]);
        Text(dc, m.Teams[0].Name, new Point(290, 345), 46, White);
        Text(dc, m.Teams[1].Name, new Point(734, 345), 46, White, right: true);
        Text(dc, "V", new Point(512, 345), 40, Dim, center: true);
        if (g.Mode == Mode.Cup && g.State == GameState.Playing)
            Text(dc, $"COMPUTER: {g.Level.ToString().ToUpperInvariant()}", new Point(512, 420), 28, Dim, center: true);
        Text(dc, $"{GameEngine.HalfChoices[g.HalfIndex]} MINUTES A HALF", new Point(512, 460), 26, Dim, center: true);
    }

    private void DrawResultCard(DrawingContext dc, string title)
    {
        var g = Engine;
        var m = g.Match!;
        dc.FillRectangle(Brush(0x80000000), new Rect(0, 0, W, H));
        Card(dc, new Rect(172, 200, 680, 320));
        Text(dc, title, new Point(512, 250), 56, Yellow, center: true);
        Text(dc, m.Teams[0].Name, new Point(300, 340), 44, White);
        Text(dc, m.Teams[1].Name, new Point(724, 340), 44, White, right: true);
        Text(dc, $"{m.Score[0]} - {m.Score[1]}", new Point(512, 400), 70, Cyan, center: true);
        if (m.Shootout) Text(dc, $"{m.PenaltyGoals(0)} - {m.PenaltyGoals(1)} ON PENALTIES", new Point(512, 460), 32, White, center: true);
        else if (title == "FULL TIME" && g.PenaltiesNext) Text(dc, "LEVEL: IT GOES TO PENALTIES", new Point(512, 470), 32, Orange, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xA0000000), new Rect(0, 0, W, H));
        Card(dc, new Rect(172, 130, 680, 480));
        if (g.Mode == Mode.Versus && g.Match is { } m)
        {
            int w = g.Winner(m);
            Text(dc, w < 0 ? "A DRAW" : $"PLAYER {w + 1} WINS", new Point(512, 200), 70, Yellow, center: true);
        }
        else
        {
            Text(dc, g.Champion ? "CUP WINNERS!" : "OUT OF THE CUP", new Point(512, 200), 70, g.Champion && (int)(_clock * 4) % 2 == 0 ? White : Yellow, center: true);
            Text(dc, $"SCORE {g.Score:N0}", new Point(512, 270), 46, Cyan, center: true);
        }
        for (int i = 0; i < g.Results.Count; i++) Text(dc, g.Results[i], new Point(512, 340 + i * 44), 32, White, center: true);
        if (g.StateTime > 1.5) Text(dc, "PRESS ENTER", new Point(512, 570), 28, White, center: true);
    }

    // ───────────────────────────── the title and choosing ─────────────────────────────

    /// <summary>An empty pitch, the view drifting slowly up and down it.</summary>
    private void DrawBackdrop(DrawingContext dc)
    {
        _camY = (Math.Sin(_clock * 0.12) * 0.5 + 0.5) * (Pitch.Length + 140 - ViewH) - 70;
        _camX = (Pitch.Width - ViewW) / 2 + Math.Sin(_clock * 0.09) * 80;
        ClampCamera();
        using (dc.PushTransform(Matrix.CreateScale(Zoom, Zoom)))
        {
            DrawPitch(dc);
            double t = _clock % 2.4 / 2.4;
            var at = new Vec(_camX + ViewW / 2 + Math.Sin(_clock * 0.7) * 150, _camY + ViewH * 0.55);
            DrawBall(dc, at, Math.Abs(Math.Sin(t * Math.PI)) * 40);
            DrawGoal(dc, Pitch.Length);
        }
        dc.FillRectangle(Brush(0x70000000), new Rect(0, 0, W, H));
    }

    private void DrawTitle(DrawingContext dc)
    {
        Text(dc, "A V A", new Point(512, 80), 40, Dim, center: true);
        Text(dc, "PITCH SIDE", new Point(512, 160), 140, Yellow, center: true);
        Text(dc, "FAST FOOTBALL FROM ABOVE", new Point(512, 248), 34, White, center: true);
        Text(dc, "ELEVEN A SIDE: TAP TO PASS, HOLD TO SHOOT, BEND IT AS IT FLIES", new Point(512, 292), 24, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(512, 380), 46, Yellow, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 672, W, 96));
        Text(dc, "PLAYER 1:  W A S D RUN   SPACE (OR F, J) KICK", new Point(512, 694), 22, 0xFFE0D8B0, center: true);
        Text(dc, "PLAYER 2:  ARROWS RUN   RIGHT CTRL (OR NUMPAD 0, /) KICK", new Point(512, 720), 22, 0xFFE0D8B0, center: true);
        Text(dc, "NO BALL: TAP TO SLIDE TACKLE OR HEAD IT   AFTER A KICK: THE STICK BENDS IT   P: PAUSE", new Point(512, 746), 18, Dim, center: true);
    }

    private void DrawSelect(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "TEAM SELECT", new Point(512, 54), 60, Yellow, center: true);
        for (int i = 0; i < League.Teams.Count; i++)
        {
            var t = League.Teams[i];
            var r = new Rect(56 + i % 4 * 232, 100 + i / 4 * 120, 216, 104);
            bool one = i == g.Pick1, two = g.Mode == Mode.Versus && i == g.Pick2;
            dc.FillRectangle(Brush(one || two ? 0xF0183018 : 0xC0101810), r, 8);
            if (one) dc.DrawRectangle(null, Pen(P1Colour, 4), r, 8, 8);
            if (two) dc.DrawRectangle(null, Pen(P2Colour, 4), r.Deflate(one ? 6 : 0), 8, 8);
            Chip(dc, new Point(r.Left + 28, r.Top + 36), t.Home);
            Text(dc, t.Name, new Point(r.Left + 46, r.Top + 32), 30, White);
            int stars = (int)Math.Round(t.Skill * 5);
            Text(dc, new string('*', stars) + new string('.', 5 - stars), new Point(r.Left + 46, r.Top + 66), 30, Yellow);
            if (one) Text(dc, "1P", new Point(r.Right - 24, r.Top + 76), 26, P1Colour, center: true);
            if (two) Text(dc, "2P", new Point(r.Right - 24, r.Top + 52), 26, P2Colour, center: true);
        }

        var rows = new List<(string Label, string Value)>
        {
            ("MODE", g.Mode == Mode.Cup ? "ONE PLAYER CUP" : "TWO PLAYERS"),
            ("PLAYER 1", League.Teams[g.Pick1].Name),
            g.Mode == Mode.Cup ? ("COMPUTER", g.Level.ToString().ToUpperInvariant()) : ("PLAYER 2", League.Teams[g.Pick2].Name),
            ("HALVES", $"{GameEngine.HalfChoices[g.HalfIndex]} MINUTES"),
            ("", "KICK OFF"),
        };
        Card(dc, new Rect(212, 362, 600, 330));
        for (int r = 0; r < rows.Count; r++)
        {
            double y = 404 + r * 56;
            bool on = r == g.SelectRow;
            if (on) dc.FillRectangle(Brush(0xFF2A4A2A), new Rect(242, y - 24, 540, 48), 6);
            if (rows[r].Label.Length > 0)
            {
                Text(dc, rows[r].Label, new Point(270, y), 34, White);
                Text(dc, (on ? "< " : "") + rows[r].Value + (on ? " >" : ""), new Point(756, y), 34, on ? Yellow : White, right: true);
            }
            else Text(dc, rows[r].Value, new Point(512, y), 44, on && (int)(_clock * 4) % 2 == 0 ? White : Yellow, center: true);
        }
        Text(dc, "UP/DOWN: CHOOSE   LEFT/RIGHT: CHANGE   ENTER: KICK OFF", new Point(512, 726), 22, Dim, center: true);
    }

    // ───────────────────────────── scores ─────────────────────────────

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        Card(dc, new Rect(182, 70, 660, 640));
        Text(dc, "HIGH SCORES", new Point(512, 124), 64, Yellow, center: true);
        Text(dc, "RANK NAME     SCORE  ROUND", new Point(512, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            string round = e.Stage > GameEngine.CupRounds ? " CUP" : $"{e.Stage,4}";
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,9:N0}   {round}", new Point(512, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(512, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        Card(dc, new Rect(182, 140, 660, 460));
        Text(dc, "A NEW HIGH SCORE!", new Point(512, 200), 64, Yellow, center: true);
        Text(dc, $"{g.Score:N0}", new Point(512, 270), 52, Cyan, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(512, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(452 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(430 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(512, 560), 26, White, center: true);
    }
}
