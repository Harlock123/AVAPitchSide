using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAPitchSide.Engine;
using AVAPitchSide.Views;

namespace AVAPitchSide.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set PITCH_SHOTS to a folder to save them
/// as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 5)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avapitch-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        // Let the camera settle on the ball first: it eases by the drawing clock.
        for (int i = 0; i < 8; i++) { c.Advance(0.15); c.InvalidateVisual(); Pump(); w.CaptureRenderedFrame(); }
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("PITCH_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAPitchSide-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer play on, both sides, until the moment comes.</summary>
    private static bool Until(GameEngine g, GameCanvas c, Func<GameEngine, bool> moment, int seconds = 600)
    {
        for (int i = 0; i < 60 * seconds; i++)
        {
            if (moment(g)) return true;
            g.Tick();
            g.ComputerPlaysOne();
            if (i % 3 == 0) c.Advance(0.05);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_team_select()
    {
        var (w, c, g) = Open();
        g.Run(20);
        c.Advance(4.25);
        Shot(w, c, "title");
        g.OpenSelect();
        g.Adjust(1, 2);
        c.Advance(0.3);
        Shot(w, c, "select");
    }

    [AvaloniaFact]
    public void The_kick_off_card()
    {
        var (w, c, g) = Open();
        g.OpenSelect();
        g.StartGame();
        g.Run(30);
        Shot(w, c, "intro");
    }

    [AvaloniaFact]
    public void Open_play()
    {
        var (w, c, g) = Open(6);
        g.Go(0, 3, Mode.Cup, Level.Normal);
        Assert.True(Until(g, c, g => g.Match is { Phase: MatchPhase.Live } m && m.Clock > 25 && m.Ball.Owner is { IsKeeper: false } &&
            g.Match.Players.Count(p => p.Pos.DistanceTo(m.Ball.Pos) < 160) >= 7));
        g.Match!.Human[0] = true;
        g.Match.Tick();
        Shot(w, c, "play");
    }

    [AvaloniaFact]
    public void A_shot_in_the_air()
    {
        var (w, c, g) = Open(7);
        g.Go(1, 5, Mode.Cup, Level.Normal);
        Assert.True(Until(g, c, g => g.Match is { Phase: MatchPhase.Live } m && m.Ball.Loose && m.Ball.Z > 14 && m.Ball.SinceKick > 0.15 &&
            m.Ball.Kicker is { } k && Math.Abs(m.Ball.Pos.Y - m.AttackGoalY(k.Side)) < 220));
        Shot(w, c, "shot");
    }

    [AvaloniaFact]
    public void A_goal_and_its_replay()
    {
        var (w, c, g) = Open(8);
        g.Go(0, 7, Mode.Cup, Level.Normal);
        Assert.True(Until(g, c, g => g.Match is { Phase: MatchPhase.Goal } m && m.PhaseTime > 0.5));
        Shot(w, c, "goal");
        Assert.True(Until(g, c, g => g.Stage == Stage.Replay && g.ReplayAt > 230));
        Shot(w, c, "replay");
    }

    [AvaloniaFact]
    public void A_corner()
    {
        var (w, c, g) = Open(9);
        g.Go(2, 4, Mode.Cup, Level.Normal);
        Assert.True(Until(g, c, g => g.Match is { Phase: MatchPhase.Restart, RestartType: Restart.Corner } m && m.PhaseTime > 0.6));
        Shot(w, c, "corner");
    }

    [AvaloniaFact]
    public void Half_time()
    {
        var (w, c, g) = Open(10);
        g.Go(0, 6, Mode.Cup, Level.Normal, halfIndex: 0);
        Assert.True(Until(g, c, g => g.Stage == Stage.HalfTime && g.StageTime > 0.5));
        Shot(w, c, "half-time");
    }

    [AvaloniaFact]
    public void A_penalty_shoot_out()
    {
        var (w, c, g) = Open(11);
        g.Go(3, 2, Mode.Cup, Level.Normal, halfIndex: 0);
        var m = g.Match!;
        m.StartShootout();
        Assert.True(Until(g, c, g => m.Penalties[0].Count + m.Penalties[1].Count >= 3 && m.Phase == MatchPhase.Live && m.PhaseTime > 0.18));
        Shot(w, c, "penalties");
    }

    [AvaloniaFact]
    public void The_cup_over_and_the_high_scores()
    {
        var (w, c, g) = Open(12);
        g.Go(0, 7, Mode.Cup, Level.Easy, halfIndex: 0);
        Assert.True(Until(g, c, g => g.Stage == Stage.FullTime));
        // A narrow defeat, keeping the goals already scored.
        g.Match!.Score[1] = g.Match.Score[0] + 1;
        Assert.True(Until(g, c, g => g.State == GameState.GameOver && g.StateTime > 2));
        Shot(w, c, "game-over");

        var (w2, c2, h) = Open();
        h.HighScores.Insert("AVA", 26000, 4);
        h.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        for (int i = 0; i < 60 * 300 && h.State != GameState.HighScores; i++) h.Tick();
        Assert.Equal(GameState.HighScores, h.State);
        c2.Advance(1.5);
        Shot(w2, c2, "high-scores");
    }
}
