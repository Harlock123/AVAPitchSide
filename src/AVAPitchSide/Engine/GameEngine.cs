namespace AVAPitchSide.Engine;

public enum GameState { Title, Select, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>Where a match is, around the football itself.</summary>
public enum Stage
{
    /// <summary>The card before the match: the round, the two teams.</summary>
    Intro,
    Play,
    /// <summary>The goal again, at half speed.</summary>
    Replay,
    HalfTime,
    /// <summary>The result (and, in the cup, penalties to come if it's level).</summary>
    FullTime,
}

public enum Mode { Cup, Versus }

public enum Level { Easy, Normal, Hard }

/// <summary>One frame of the match kept for the replay: where everyone was and what they were doing.</summary>
public sealed record Frame(Vec[] Pos, Vec[] Facing, PState[] State, double[] Anim, Vec Ball, double BallZ);

/// <summary>
/// The whole game with no UI: the title, choosing teams, the one-player cup against the computer, the two-player
/// friendly, the demonstration, replays of goals, scoring and the high-score table. <see cref="Update"/> runs in
/// fixed 60 Hz steps.
/// </summary>
public sealed class GameEngine
{
    public const double Step = Match.Step;
    public const double TitleIdleSeconds = 16, HighScoreSeconds = 10, IntroSeconds = 2.6, HalfTimeSeconds = 3.2;
    public const double FullTimeSeconds = 4.2, OverSeconds = 7, AttractHalf = 40;
    public const int CupRounds = 3, GoalPoints = 500, WinPoints = 2000, CupPoints = 10000;
    public static readonly int[] HalfChoices = [2, 3, 4];
    public static readonly string[] RoundNames = ["QUARTER-FINAL", "SEMI-FINAL", "THE FINAL"];

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true;
    private Controls _menuPrev;
    private readonly Random _random;
    private readonly List<Frame> _recent = [];
    private int _goalAt = -1, _seenEvents;

    public GameEngine(HighScoreTable? table = null, int? seed = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        _random = seed is { } s ? new Random(s) : new Random();
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Stage Stage { get; private set; }
    public double StateTime { get; private set; }
    public double StageTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }

    // The select screen.
    public Mode Mode { get; private set; } = Mode.Cup;
    public int Pick1 { get; private set; }
    public int Pick2 { get; private set; } = 1;
    public Level Level { get; private set; } = Level.Normal;
    public int HalfIndex { get; private set; } = 1;
    public int SelectRow { get; private set; }
    public const int SelectRows = 5;
    public double HalfSeconds => HalfChoices[HalfIndex] * 60;

    public Match? Match { get; private set; }
    public int Round { get; private set; }
    public int Score { get; private set; }
    public bool Champion { get; private set; }
    public IReadOnlyList<Team> Opponents { get; private set; } = [];
    /// <summary>The cup's results so far, as text.</summary>
    public List<string> Results { get; } = [];
    public string? Banner { get; private set; }
    public double BannerTime { get; private set; }

    public IReadOnlyList<Frame>? Replay { get; private set; }
    public double ReplayAt { get; private set; }
    public Frame? ReplayFrame => Replay is { Count: > 0 } r ? r[Math.Min(r.Count - 1, (int)ReplayAt)] : null;

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c1, Controls c2, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Step)
        {
            _accumulator -= Step;
            Tick(c1, c2, start);
        }
    }

    public void Tick(Controls c1 = default, Controls c2 = default, bool start = false)
    {
        StateTime += Step;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        bool accept = c1.Kick && !_menuPrev.Kick;
        if (BannerTime > 0 && (BannerTime -= Step) <= 0) Banner = null;
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += Step;
                if (startPressed || accept) OpenSelect();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Select:
                SelectStep(c1, startPressed, accept);
                break;
            case GameState.Attract:
                Run(default, default, false);
                if (startPressed || accept) OpenSelect();
                break;
            case GameState.Playing:
                Run(c1, c2, startPressed || accept);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds || (startPressed || accept) && StateTime > 1.5) AfterGameOver();
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Champion ? CupRounds + 1 : Round);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed || accept) OpenSelect();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
        _menuPrev = c1;
    }

    // ───────────────────────────── choosing ─────────────────────────────

    public void OpenSelect()
    {
        State = GameState.Select;
        StateTime = 0;
        SelectRow = 1;
        _menuPrev = new Controls(1, 1, true);
        Play(Sfx.MusicStop);
        Play(Sfx.Select);
    }

    private void SelectStep(Controls c, bool startPressed, bool accept)
    {
        int move = c.Y != _menuPrev.Y ? c.Y : 0;
        int turn = c.X != _menuPrev.X ? c.X : 0;
        if (move != 0)
        {
            SelectRow = (SelectRow - move + SelectRows) % SelectRows;
            Play(Sfx.Select);
        }
        if (turn != 0) { Adjust(SelectRow, turn); Play(Sfx.Select); }
        if (startPressed || accept && SelectRow == SelectRows - 1) StartGame();
        else if (accept) { Adjust(SelectRow, 1); Play(Sfx.Select); }
    }

    /// <summary>Changes a select row one step: the mode, player 1's team, the computer's level or player 2's team, the half's length.</summary>
    public void Adjust(int row, int by)
    {
        int n = League.Teams.Count;
        switch (row)
        {
            case 0: Mode = Mode == Mode.Cup ? Mode.Versus : Mode.Cup; break;
            case 1: Pick1 = (Pick1 + by + n) % n; if (Pick1 == Pick2 && Mode == Mode.Versus) Pick2 = (Pick2 + by + n) % n; break;
            case 2 when Mode == Mode.Cup: Level = (Level)(((int)Level + by + 3) % 3); break;
            case 2: Pick2 = (Pick2 + by + n) % n; if (Pick2 == Pick1) Pick2 = (Pick2 + by + n) % n; break;
            case 3: HalfIndex = (HalfIndex + by + HalfChoices.Length) % HalfChoices.Length; break;
        }
    }

    /// <summary>The computer's level in a cup round: it grows round by round, with the team's own quality mixed in.</summary>
    public static double SkillOf(Level level, int round, Team team) =>
        Math.Clamp((level switch { Level.Easy => 0.25, Level.Normal => 0.5, _ => 0.78 } + 0.07 * (round - 1)) * 0.75 + team.Skill * 0.25, 0, 1);

    public void StartGame()
    {
        Score = 0;
        Champion = false;
        NewEntryIndex = -1;
        Results.Clear();
        State = GameState.Playing;
        StateTime = 0;
        Round = 1;
        var mine = League.Teams[Pick1];
        Opponents = Mode == Mode.Cup ? League.Teams.Where(t => t != mine).OrderBy(_ => _random.Next()).Take(CupRounds).ToList() : [League.Teams[Pick2]];
        BeginMatch();
    }

    private void StartAttract()
    {
        var two = League.Teams.OrderBy(_ => _random.Next()).Take(2).ToList();
        State = GameState.Attract;
        StateTime = 0;
        Round = 1;
        Opponents = [two[1]];
        Match = new Match(two[0], two[1], AttractHalf, _random);
        NewMatch();
    }

    private void BeginMatch()
    {
        var mine = League.Teams[Pick1];
        var them = Opponents[Round - 1];
        bool versus = Mode == Mode.Versus;
        Match = new Match(mine, them, HalfSeconds, _random, humanHome: true, humanAway: versus);
        if (!versus)
        {
            Match.SetSkill(0, 0.55 + mine.Skill * 0.3);
            Match.SetSkill(1, SkillOf(Level, Round, them));
        }
        NewMatch();
    }

    private void NewMatch()
    {
        _recent.Clear();
        _goalAt = -1;
        _seenEvents = 0;
        Replay = null;
        SetStage(Stage.Intro);
        Play(Sfx.MusicStop);
    }

    private void SetStage(Stage s)
    {
        Stage = s;
        StageTime = 0;
    }

    // ───────────────────────────── a match ─────────────────────────────

    private void Run(Controls c1, Controls c2, bool skip)
    {
        StageTime += Step;
        var m = Match!;
        switch (Stage)
        {
            case Stage.Intro:
                if (StageTime > IntroSeconds || skip && StageTime > 0.6) { SetStage(Stage.Play); Play(Sfx.Crowd); }
                break;
            case Stage.Replay:
                ReplayAt += 0.5;
                if (ReplayAt >= Replay!.Count || skip && ReplayAt > 20) { Replay = null; SetStage(Stage.Play); }
                break;
            case Stage.HalfTime:
                if (StageTime > HalfTimeSeconds || skip && StageTime > 1) { m.StartSecondHalf(); SetStage(Stage.Play); Play(Sfx.Crowd); }
                break;
            case Stage.FullTime:
                if (StageTime > FullTimeSeconds || skip && StageTime > 1.5) AfterMatch();
                break;
            case Stage.Play:
                m.Tick(c1, c2);
                Record(m);
                foreach (var s in m.Heard) Play(s);
                m.Heard.Clear();
                for (; _seenEvents < m.Events.Count; _seenEvents++)
                {
                    var e = m.Events[_seenEvents];
                    if (e.Kind == "goal")
                    {
                        _goalAt = _recent.Count;
                        if (State == GameState.Playing && Mode == Mode.Cup && e.Side == 0) Score += GoalPoints;
                    }
                    Banner = e.Text;
                    BannerTime = e.Kind == "goal" ? 2.4 : 1.6;
                }
                if (m.Phase == MatchPhase.Goal && _goalAt >= 0 && m.PhaseTime > 1.9 && !m.Shootout)
                {
                    int from = Math.Max(0, _goalAt - 200);
                    Replay = _recent.GetRange(from, _recent.Count - from);
                    ReplayAt = 0;
                    _goalAt = -1;
                    SetStage(Stage.Replay);
                }
                else if (m.Phase == MatchPhase.HalfOver && m.PhaseTime > 1.5) { SetStage(Stage.HalfTime); Play(Sfx.MusicStop); }
                else if (m.Phase == MatchPhase.Over && m.PhaseTime > 1.5) { SetStage(Stage.FullTime); Play(Sfx.MusicStop); }
                break;
        }
    }

    private void Record(Match m)
    {
        var ps = m.Players;
        _recent.Add(new Frame(ps.Select(p => p.Pos).ToArray(), ps.Select(p => p.Facing).ToArray(), ps.Select(p => p.State).ToArray(),
            ps.Select(p => p.Anim).ToArray(), m.Ball.Pos, m.Ball.Z));
        if (_recent.Count > 420)
        {
            _recent.RemoveRange(0, 60);
            if (_goalAt >= 0) _goalAt = Math.Max(0, _goalAt - 60);
        }
    }

    /// <summary>The match's winner (0 or 1, the shoot-out counting), or −1 for a draw.</summary>
    public int Winner(Match m) => m.ShootWinner ?? (m.Score[0] > m.Score[1] ? 0 : m.Score[1] > m.Score[0] ? 1 : -1);

    /// <summary>Whether the result card is about to send this level cup tie to penalties.</summary>
    public bool PenaltiesNext => Match is { } m && Mode == Mode.Cup && State == GameState.Playing && !m.Shootout && m.Score[0] == m.Score[1];

    private void AfterMatch()
    {
        var m = Match!;
        if (PenaltiesNext)
        {
            m.StartShootout();
            SetStage(Stage.Play);
            Play(Sfx.Crowd);
            return;
        }
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        int w = Winner(m);
        string pens = m.Shootout ? $" ({m.PenaltyGoals(0)}-{m.PenaltyGoals(1)} PENS)" : "";
        Results.Add($"{RoundNamesFor(Round)}: {m.Teams[0].Short} {m.Score[0]}-{m.Score[1]} {m.Teams[1].Short}{pens}");
        if (Mode == Mode.Versus) { EndGame(); return; }
        if (w != 0) { Play(Sfx.Defeat); EndGame(); return; }
        Score += WinPoints * Round + Math.Max(0, m.Score[0] - m.Score[1]) * 250;
        if (Round >= CupRounds)
        {
            Champion = true;
            Score += CupPoints;
            Play(Sfx.CupWin);
            EndGame();
            return;
        }
        Round++;
        BeginMatch();
    }

    public string RoundNamesFor(int round) => Mode == Mode.Versus || State == GameState.Attract ? "FRIENDLY" : RoundNames[Math.Clamp(round - 1, 0, 2)];

    private void EndGame()
    {
        State = GameState.GameOver;
        StateTime = 0;
        Play(Sfx.MusicStop);
    }

    private void AfterGameOver()
    {
        if (Mode == Mode.Cup && HighScores.Qualifies(Score))
        {
            State = GameState.EnterInitials;
            StateTime = 0;
            Initials = new InitialsEntry(HighScores.LastInitials);
        }
        else ShowHighScores(-1);
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Play(Sfx.MusicStop);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
        Play(Sfx.MusicStop);
    }

    public void Pause()
    {
        if (State == GameState.Playing) { State = GameState.Paused; Play(Sfx.MusicStop); }
    }

    public void Resume()
    {
        if (State == GameState.Paused) { State = GameState.Playing; if (Stage == Stage.Play) Play(Sfx.Crowd); }
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c1 = default, Controls c2 = default)
    {
        for (int i = 0; i < steps; i++) Tick(c1, c2);
    }

    /// <summary>Straight into a match, past the select screen and the card.</summary>
    public void Go(int home, int away, Mode mode = Mode.Cup, Level level = Level.Normal, int halfIndex = 1)
    {
        Mode = mode;
        Level = level;
        Pick1 = home;
        Pick2 = away;
        HalfIndex = halfIndex;
        StartGame();
        if (mode == Mode.Cup)
        {
            Opponents = [League.Teams[away], .. Opponents.Where(t => t != League.Teams[away]).Take(CupRounds - 1)];
            BeginMatch();
        }
        SetStage(Stage.Play);
    }

    /// <summary>Hands player 1's side to the computer (for the demonstration and tests).</summary>
    public void ComputerPlaysOne()
    {
        if (Match != null) Match.Human[0] = false;
    }
}
