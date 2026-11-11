using Avalonia.Input;
using AVAPitchSide.Engine;

namespace AVAPitchSide.Tests;

public sealed class GameplayTests
{
    private static GameEngine NewEngine(int seed = 3) =>
        new(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avapitch-test-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };

    /// <summary>A match with player 1 at the controls of the home side, which attacks up the screen (toward y = 0) first.</summary>
    private static Match NewMatch(bool human = true, int seed = 1, bool humanAway = false) =>
        new(League.Teams[0], League.Teams[4], 180, new Random(seed), humanHome: human, humanAway: humanAway);

    private static Player Mid(Match m, int side = 0) => m.Side(side).First(p => p.Role == Role.Midfielder && p.Index == 6);
    private static Player Fwd(Match m, int side = 0) => m.Side(side).First(p => p.Role == Role.Forward);

    /// <summary>Sends one side's outfield players off to a far corner, out of the way.</summary>
    private static void Clear(Match m, int side, Vec to)
    {
        foreach (var p in m.Side(side).Where(p => !p.IsKeeper)) { p.Pos = to; p.Vel = Vec.Zero; }
    }

    private static void Run(Match m, int frames, Controls c0 = default, Controls c1 = default)
    {
        for (int i = 0; i < frames; i++) m.Tick(c0, c1);
    }

    [Fact]
    public void Up_on_the_keys_runs_up_the_screen()
    {
        using var input = new InputHandler();
        var m = NewMatch();
        var me = Mid(m);
        me.Pos = new Vec(400, 800);
        m.Give(me);
        input.OnKeyDown(Key.W);
        Assert.Equal(1, input.KeysOne.Y);
        double y = me.Pos.Y;
        Run(m, 30, input.KeysOne);
        Assert.Same(me, m.Controlled[0]);
        Assert.True(me.Pos.Y < y - 40, "up the screen is toward y = 0");
        Assert.Same(me, m.Ball.Owner);
        Assert.True(m.Ball.Pos.Y < me.Pos.Y, "the ball runs ahead of the feet");
    }

    [Fact]
    public void A_tap_on_the_kick_key_passes_to_a_team_mate()
    {
        using var input = new InputHandler();
        var m = NewMatch();
        Clear(m, 1, new Vec(20, 1200));
        var me = Mid(m);
        var mate = Fwd(m);
        me.Pos = new Vec(400, 800);
        me.Facing = new Vec(0, -1);
        mate.Pos = new Vec(420, 620);
        m.Give(me);
        input.OnKeyDown(Key.Space);
        Run(m, 3, input.KeysOne);
        input.OnKeyUp(Key.Space);
        Run(m, 1, input.KeysOne);
        Assert.Null(m.Ball.Owner);
        Assert.Same(me, m.Ball.Kicker);
        Run(m, 150);
        Assert.Equal(0, m.Ball.Owner?.Side);
        Assert.NotSame(me, m.Ball.Owner);
    }

    [Fact]
    public void A_held_kick_shoots_and_a_goal_counts()
    {
        using var input = new InputHandler();
        var m = NewMatch();
        Clear(m, 1, new Vec(20, 1200));
        var k = m.Keeper(1);
        k.Pos = new Vec(80, 30);
        var me = Fwd(m);
        me.Pos = new Vec(Pitch.Width / 2, 170);
        me.Facing = new Vec(0, -1);
        m.Give(me);
        input.OnKeyDown(Key.Space);
        Run(m, 20, input.KeysOne);
        input.OnKeyUp(Key.Space);
        Run(m, 60, input.KeysOne);
        Assert.Equal(1, m.Score[0]);
        Assert.Equal(MatchPhase.Goal, m.Phase);
        Assert.Contains(m.Events, e => e.Kind == "goal" && e.Side == 0);
        Run(m, 60 * 3);
        Assert.Equal(MatchPhase.Restart, m.Phase);
        Assert.Equal(Restart.Kickoff, m.RestartType);
        Assert.Equal(1, m.RestartSide);
    }

    [Fact]
    public void A_rolling_ball_slows_and_stops_where_friction_says()
    {
        var m = NewMatch(human: false);
        m.Freeze();
        foreach (var p in m.Players) p.Pos = new Vec(-60, -60);
        m.Loose(new Vec(400, 900), new Vec(0, -210));
        var start = m.Ball.Pos;
        m.Players.ForEach(p => p.Speed = 0);
        Run(m, 60 * 6);
        Assert.Equal(Vec.Zero, m.Ball.Vel);
        Assert.InRange(start.Y - m.Ball.Pos.Y, 210 / Match.Friction - 12, 210 / Match.Friction + 2);
    }

    [Fact]
    public void A_ball_in_the_air_comes_down_and_bounces()
    {
        var m = NewMatch(human: false);
        m.Players.ForEach(p => { p.Pos = new Vec(-60, -60); p.Speed = 0; });
        m.Loose(new Vec(400, 900), new Vec(0, -100), 0.5, 180);
        double top = 0;
        bool bounced = false;
        for (int i = 0; i < 120; i++)
        {
            double vz = m.Ball.Vz;
            m.Tick();
            top = Math.Max(top, m.Ball.Z);
            if (vz < 0 && m.Ball.Vz > 0) bounced = true;
        }
        Assert.InRange(top, 180 * 180 / (2 * Pitch.Gravity) - 3, 180 * 180 / (2 * Pitch.Gravity) + 1);
        Assert.True(bounced);
    }

    [Fact]
    public void Aftertouch_bends_the_shot()
    {
        using var input = new InputHandler();
        double Sideways(Key? hold)
        {
            var m = NewMatch();
            Clear(m, 1, new Vec(20, 1200));
            m.Keeper(1).Pos = new Vec(-40, -40);
            var me = Fwd(m);
            me.Pos = new Vec(Pitch.Width / 2, 600);
            me.Facing = new Vec(0, -1);
            m.Give(me);
            input.OnKeyDown(Key.Space);
            Run(m, 12, input.KeysOne);
            input.OnKeyUp(Key.Space);
            if (hold is { } h) input.OnKeyDown(h);
            Run(m, 30, input.KeysOne);
            if (hold is { } h2) input.OnKeyUp(h2);
            return m.Ball.Pos.X - Pitch.Width / 2;
        }
        Assert.InRange(Sideways(null), -3, 3);
        Assert.True(Sideways(Key.D) > 15, "right on the stick curls it right");
        Assert.True(Sideways(Key.A) < -15);
    }

    [Fact]
    public void The_keeper_holds_a_shot_at_him()
    {
        var m = NewMatch(human: false);
        var k = m.Keeper(1);
        Assert.True(k.Pos.Y < 40, "the away side defends the top goal first");
        var striker = Fwd(m);
        striker.Pos = new Vec(Pitch.Width / 2, 220);
        m.Loose(new Vec(Pitch.Width / 2, 210), Vec.Zero);
        m.Strike(striker, (k.Pos - m.Ball.Pos).Norm * 330, 20, Sfx.KickHard);
        Run(m, 60);
        Assert.Same(k, m.Ball.Owner);
        Assert.Equal(PState.Hold, k.State);
        Assert.Equal(0, m.Score[0]);
    }

    [Fact]
    public void A_keeper_cannot_handle_a_pass_from_his_own_side()
    {
        var m = NewMatch(human: false);
        Clear(m, 0, new Vec(20, 1200));
        var k = m.Keeper(1);
        var back = m.Side(1).First(p => p.Role == Role.Defender);
        back.Pos = new Vec(Pitch.Width / 2, 200);
        m.Loose(new Vec(Pitch.Width / 2, 190), Vec.Zero);
        m.Strike(back, (k.Pos - m.Ball.Pos).Norm * 260, 0, Sfx.Kick);
        Run(m, 50);
        Assert.NotEqual(PState.Hold, k.State);
        Assert.DoesNotContain(Sfx.Catch, m.Heard);
    }

    [Fact]
    public void Over_the_touchline_is_a_throw_in_to_the_other_side()
    {
        var m = NewMatch(human: false);
        var p = Mid(m);
        m.Loose(new Vec(30, 600), new Vec(-300, 0));
        m.Ball.LastTouch = p;
        Run(m, 20);
        Assert.Equal(MatchPhase.Stoppage, m.Phase);
        Run(m, 80);
        Assert.Equal(MatchPhase.Restart, m.Phase);
        Assert.Equal(Restart.ThrowIn, m.RestartType);
        Assert.Equal(1, m.RestartSide);
        Assert.Equal(0, m.Ball.Pos.X);
    }

    [Fact]
    public void Over_the_goal_line_is_a_goal_kick_or_a_corner()
    {
        // The home side attacks the top goal: their shot wide is a goal kick to the away side...
        var m = NewMatch(human: false);
        m.Loose(new Vec(Pitch.Width / 2 + 150, 40), new Vec(0, -300));
        m.Ball.LastTouch = Fwd(m, 0);
        Run(m, 100);
        Assert.Equal(Restart.GoalKick, m.RestartType);
        Assert.Equal(1, m.RestartSide);
        Assert.Same(m.Keeper(1), m.Taker);

        // ...and the away side putting it behind their own line is a corner to the home side.
        var c = NewMatch(human: false);
        c.Loose(new Vec(Pitch.Width / 2 + 150, 40), new Vec(0, -300));
        c.Ball.LastTouch = c.Side(1).First(p => p.Role == Role.Defender);
        Run(c, 100);
        Assert.Equal(Restart.Corner, c.RestartType);
        Assert.Equal(0, c.RestartSide);
        Assert.Equal(Pitch.Width - 2, c.Ball.Pos.X);
    }

    [Fact]
    public void A_slide_that_takes_the_ball_is_fair_and_one_from_behind_is_a_foul()
    {
        Match Slide(Vec runnerFacing)
        {
            // The runner is player 2's, standing still with the ball.
            var m = NewMatch(human: false, humanAway: true);
            var runner = Mid(m, 1);
            runner.Pos = new Vec(400, 700);
            runner.Facing = runnerFacing;
            m.Give(runner);
            var tackler = Mid(m, 0);
            tackler.Pos = new Vec(400, 745);
            tackler.Facing = new Vec(0, -1);
            m.StartSlide(tackler);
            Run(m, 20);
            return m;
        }
        // Coming toward the tackler, the ball is between them: the slide takes it cleanly.
        var front = Slide(new Vec(0, 1));
        Assert.DoesNotContain(front.Events, e => e.Kind == "foul");
        Assert.True(Mid(front, 0).SlideTouched, "the slide got the ball");
        // Running away from him, the man is in the way: a foul.
        var behind = Slide(new Vec(0, -1));
        Assert.Contains(behind.Events, e => e.Kind == "foul");
        Assert.Equal(PState.Down, Mid(behind, 1).State);
    }

    [Fact]
    public void A_foul_gives_a_free_kick_and_in_the_area_a_penalty()
    {
        // The fouled player is player 1's, standing still with the ball.
        var m = NewMatch();
        var victim = Fwd(m, 0);
        victim.Pos = new Vec(Pitch.Width / 2, 120);
        victim.Facing = new Vec(0, -1);
        m.Give(victim);
        var defender = m.Side(1).First(p => p.Role == Role.Defender);
        defender.Pos = new Vec(Pitch.Width / 2, 150);
        defender.Facing = new Vec(0, -1);
        m.StartSlide(defender);
        Run(m, 90);
        Assert.Equal(MatchPhase.Restart, m.Phase);
        Assert.Equal(Restart.Penalty, m.RestartType);
        Assert.Equal(0, m.RestartSide);
        Assert.Equal(Pitch.PenaltySpot, m.Ball.Pos.Y);

        var w = NewMatch();
        var v2 = Mid(w, 0);
        v2.Pos = new Vec(300, 700);
        v2.Facing = new Vec(0, -1);
        w.Give(v2);
        var d2 = Mid(w, 1);
        d2.Pos = new Vec(300, 730);
        d2.Facing = new Vec(0, -1);
        w.StartSlide(d2);
        Run(w, 90);
        Assert.Equal(Restart.FreeKick, w.RestartType);
        Assert.Equal(0, w.RestartSide);
    }

    [Fact]
    public void A_standing_tackle_wins_the_ball_in_the_end()
    {
        var m = NewMatch(human: false, seed: 5, humanAway: true);
        var owner = Mid(m, 1);
        owner.Pos = new Vec(400, 700);
        m.Give(owner);
        var tackler = Mid(m, 0);
        int won = 0;
        for (int i = 0; i < 240 && m.Ball.Owner?.Side != 0 && m.Ball.LastTouch?.Side != 0; i++)
        {
            tackler.Pos = m.Ball.Pos + new Vec(0, 4);
            m.Tick();
            won = i;
        }
        Assert.True(m.Ball.LastTouch?.Side == 0, "the tackler gets a foot to it");
        Assert.True(won > 20, "not at once: the owner has a moment");
    }

    [Fact]
    public void The_computer_plays_a_whole_match_with_restarts_and_shots()
    {
        var m = NewMatch(human: false, seed: 7);
        var restarts = new HashSet<Restart>();
        int shots = 0;
        for (int i = 0; i < 60 * 500 && m.Phase != MatchPhase.Over; i++)
        {
            m.Tick();
            if (m.Phase == MatchPhase.HalfOver && m.PhaseTime > 1) m.StartSecondHalf();
            if (m.Phase == MatchPhase.Restart) restarts.Add(m.RestartType);
            shots += m.Heard.Count(s => s == Sfx.KickHard);
            m.Heard.Clear();
        }
        Assert.Equal(MatchPhase.Over, m.Phase);
        Assert.Equal(2, m.Half);
        Assert.True(shots > 8, $"shots: {shots}");
        Assert.Contains(Restart.Kickoff, restarts);
        Assert.True(restarts.Count >= 3, string.Join(",", restarts));
    }

    [Fact]
    public void Ends_swap_at_half_time()
    {
        var m = NewMatch(human: false);
        Assert.Equal(-1, m.Dir(0));
        Assert.Equal(Pitch.Length, m.OwnGoalY(0));
        m.StartSecondHalf();
        Assert.Equal(1, m.Dir(0));
        Assert.Equal(0, m.OwnGoalY(0));
        Assert.Equal(1, m.RestartSide);
        Assert.True(m.Keeper(0).Pos.Y < 40);
    }

    [Fact]
    public void A_shoot_out_finds_a_winner()
    {
        var m = NewMatch(human: false, seed: 9);
        m.StartShootout();
        for (int i = 0; i < 60 * 200 && m.Phase != MatchPhase.Over; i++) m.Tick();
        Assert.Equal(MatchPhase.Over, m.Phase);
        Assert.NotNull(m.ShootWinner);
        int w = m.ShootWinner!.Value;
        Assert.True(m.PenaltyGoals(w) > m.PenaltyGoals(1 - w));
        Assert.True(m.Penalties[0].Count >= 3);
        Assert.InRange(m.Penalties[0].Count - m.Penalties[1].Count, 0, 1);
    }

    /// <summary>Runs the engine on (the computer playing for player 1 too) until the full-time card.</summary>
    private static void ToFullTime(GameEngine g)
    {
        g.ComputerPlaysOne();
        for (int i = 0; i < 60 * 700 && g.Stage != Stage.FullTime; i++) { g.Tick(); if (g.Stage == Stage.Play) g.ComputerPlaysOne(); }
        Assert.Equal(Stage.FullTime, g.Stage);
    }

    [Fact]
    public void Winning_a_cup_tie_goes_on_to_the_next_round()
    {
        var g = NewEngine();
        g.Go(0, 7, Mode.Cup, Level.Easy, halfIndex: 0);
        ToFullTime(g);
        g.Match!.Score[0] = 3;
        g.Match.Score[1] = 1;
        g.Run(60 * 5);
        Assert.Equal(2, g.Round);
        Assert.Equal(Stage.Intro, g.Stage);
        Assert.True(g.Score >= GameEngine.WinPoints);
        Assert.Single(g.Results);
    }

    [Fact]
    public void A_level_cup_tie_goes_to_penalties()
    {
        var g = NewEngine(4);
        g.Go(1, 6, Mode.Cup, Level.Normal, halfIndex: 0);
        ToFullTime(g);
        g.Match!.Score[0] = g.Match.Score[1] = 1;
        Assert.True(g.PenaltiesNext);
        g.Run(60 * 5);
        Assert.True(g.Match.Shootout);
        ToFullTime(g);
        Assert.NotNull(g.Match.ShootWinner);
        g.Run(60 * 5);
        Assert.True(g.Round == 2 || g.State == GameState.GameOver);
    }

    [Fact]
    public void Losing_ends_the_cup_and_a_good_score_asks_for_initials()
    {
        var g = NewEngine();
        g.Go(0, 7, Mode.Cup, Level.Easy, halfIndex: 0);
        ToFullTime(g);
        g.Match!.Score[0] = 0;
        g.Match.Score[1] = 2;
        g.Run(60 * 5);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run(60 * 8);
        Assert.Equal(g.HighScores.Qualifies(g.Score) ? GameState.EnterInitials : GameState.HighScores, g.State);
    }

    [Fact]
    public void A_goal_is_shown_again()
    {
        var g = NewEngine();
        g.Go(0, 7, Mode.Versus, halfIndex: 0);
        var m = g.Match!;
        Clear(m, 1, new Vec(20, 1200));
        m.Keeper(1).Pos = new Vec(60, 20);
        var me = Fwd(m);
        me.Pos = new Vec(Pitch.Width / 2, 150);
        m.Give(me);
        m.Strike(me, new Vec(0, -500), 10, Sfx.KickHard);
        for (int i = 0; i < 60 * 4 && g.Stage != Stage.Replay; i++) g.Tick();
        Assert.Equal(Stage.Replay, g.Stage);
        Assert.NotNull(g.ReplayFrame);
        for (int i = 0; i < 60 * 12 && g.Stage == Stage.Replay; i++) g.Tick();
        Assert.Equal(Stage.Play, g.Stage);
    }

    [Fact]
    public void The_select_screen_changes_mode_teams_level_and_halves()
    {
        var g = NewEngine();
        g.OpenSelect();
        g.Adjust(1, 1);
        Assert.Equal(1, g.Pick1);
        g.Adjust(2, 1);
        Assert.Equal(Level.Hard, g.Level);
        g.Adjust(3, 1);
        Assert.Equal(4 * 60, g.HalfSeconds);
        g.Adjust(0, 1);
        Assert.Equal(Mode.Versus, g.Mode);
        g.Adjust(2, -1);
        Assert.Equal(0, g.Pick2);
        g.StartGame();
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Match!.Human[0] && g.Match.Human[1]);
    }

    [Fact]
    public void The_computer_gets_harder_up_the_cup()
    {
        var t = League.Teams[3];
        Assert.True(GameEngine.SkillOf(Level.Easy, 1, t) < GameEngine.SkillOf(Level.Normal, 1, t));
        Assert.True(GameEngine.SkillOf(Level.Normal, 1, t) < GameEngine.SkillOf(Level.Normal, 3, t));
    }

    [Fact]
    public void Teams_are_original_and_away_strips_avoid_a_clash()
    {
        Assert.Equal(8, League.Teams.Count);
        Assert.All(League.Teams, t => Assert.Equal(11, t.Players.Distinct().Count()));
        // Two sides in the same strip would clash: the second wears its other one.
        var a = League.Teams[5];
        var copy = League.Teams[2] with { Home = a.Home };
        Assert.Equal(copy.Away, League.AwayKit(a, copy));
        Assert.Equal(League.Teams[1].Home, League.AwayKit(League.Teams[0], League.Teams[1]));
    }
}
