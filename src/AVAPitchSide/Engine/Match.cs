namespace AVAPitchSide.Engine;

public enum Role { Keeper, Defender, Midfielder, Forward }

public enum PState
{
    Run,
    /// <summary>A sliding tackle: skidding along the grass, then getting up.</summary>
    Slide,
    /// <summary>On the floor after a tackle or a foul.</summary>
    Down,
    /// <summary>A keeper's dive.</summary>
    Dive,
    /// <summary>A keeper with the ball in his hands.</summary>
    Hold,
    Celebrate,
}

public enum Restart { Kickoff, ThrowIn, Corner, GoalKick, FreeKick, Penalty }

public enum MatchPhase
{
    /// <summary>The ball placed for a restart, waiting for the taker.</summary>
    Restart,
    Live,
    /// <summary>The whistle has gone (ball out, a foul): a moment before the restart.</summary>
    Stoppage,
    /// <summary>A goal: the celebration.</summary>
    Goal,
    HalfOver,
    Over,
}

/// <summary>Sounds the match asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Kick, KickHard, Bounce, Post, Header, Tackle, Whistle, WhistleLong, Goal, Save, Catch, Throw, Miss, Select,
    Crowd, TitleTune, CupWin, Defeat, MusicStop,
}

/// <summary>One frame of a player's controls: the stick (screen right +1, up +1) and the one button.</summary>
public readonly record struct Controls(int X = 0, int Y = 0, bool Kick = false)
{
    /// <summary>The stick as a direction on the pitch, where up the screen is −y.</summary>
    public Vec Direction => new Vec(X, -Y).Norm;
}

public sealed class Player
{
    public required int Side;
    public required int Index;
    public required string Name;
    public required Role Role;
    /// <summary>Where the player belongs in the formation: across (0 left to 1 right, attacking up) and depth (0 own goal to 1 theirs).</summary>
    public required Vec Home;
    public Vec Pos, Vel, Facing = new(0, -1);
    public PState State;
    public double StateTime;
    public double Speed = 135;
    /// <summary>The running animation's phase.</summary>
    public double Anim;
    public bool SlideTouched;
    /// <summary>When the computer next makes up its mind about the ball at its feet.</summary>
    public double Think;
    public Vec Dribble;
    public int Number => Index + 1;
    public bool IsKeeper => Role == Role.Keeper;
    public bool Free => State == PState.Run;
}

public sealed class Ball
{
    public Vec Pos, Vel, Spin;
    public double Z, Vz;
    public Player? Owner;
    public Player? LastTouch;
    public Player? Kicker;
    /// <summary>The team-mate a pass was played to, who goes to meet it.</summary>
    public Player? Target;
    public double SinceKick = 9;
    /// <summary>How long the owner has had it.</summary>
    public double Owned;
    public bool Loose => Owner == null;
}

public readonly record struct MatchEvent(string Kind, int Side, string Text);

/// <summary>
/// A football match, a step at a time (60 a second): eleven a side, the ball's flight and bounce and spin, dribbling,
/// passing and shooting with aftertouch, tackles and fouls, the restarts, the keepers, the clock, and a penalty
/// shoot-out when a cup match is drawn. Either side can be a person (<see cref="Tick"/> takes their controls) or the
/// computer.
/// </summary>
public sealed partial class Match
{
    public const double Step = 1.0 / 60;
    public const double TapTime = 0.14, FullCharge = 0.55, Aftertouch = 0.4;

    public Team[] Teams { get; }
    public Kit[] Kits { get; }
    public List<Player> Players { get; } = [];
    public Ball Ball { get; } = new();
    public int[] Score { get; } = new int[2];
    public bool[] Human { get; } = new bool[2];
    public double[] Skill { get; } = new double[2];
    public Player?[] Controlled { get; } = new Player?[2];
    public List<Sfx> Heard { get; } = [];
    public List<MatchEvent> Events { get; } = [];

    public int Half { get; private set; } = 1;
    /// <summary>Seconds of the half gone.</summary>
    public double Clock { get; private set; }
    public double HalfLength { get; }
    public MatchPhase Phase { get; private set; }
    public double PhaseTime { get; private set; }
    public Restart RestartType { get; private set; }
    public int RestartSide { get; private set; }
    public Player? Taker { get; private set; }
    public Player? Scorer { get; private set; }
    public int KickoffSide { get; private set; }

    private readonly Random _random;
    private readonly HumanState[] _human = [new(), new()];
    private (Restart Type, int Side, Vec Spot)? _pending;
    private int _lastGoalSide;

    private sealed class HumanState
    {
        public bool Prev, Charging;
        public double Charge, After;
        public Vec KickDir;
    }

    public Match(Team home, Team away, double halfSeconds, Random random, bool humanHome = false, bool humanAway = false)
    {
        Teams = [home, away];
        Kits = [home.Home, League.AwayKit(home, away)];
        HalfLength = halfSeconds;
        _random = random;
        Human[0] = humanHome;
        Human[1] = humanAway;
        Skill[0] = home.Skill;
        Skill[1] = away.Skill;
        // A flat four-four-two.
        (Role R, double A, double D)[] shape =
        [
            (Role.Keeper, 0.5, 0.02),
            (Role.Defender, 0.14, 0.2), (Role.Defender, 0.38, 0.16), (Role.Defender, 0.62, 0.16), (Role.Defender, 0.86, 0.2),
            (Role.Midfielder, 0.14, 0.44), (Role.Midfielder, 0.38, 0.4), (Role.Midfielder, 0.62, 0.4), (Role.Midfielder, 0.86, 0.44),
            (Role.Forward, 0.38, 0.66), (Role.Forward, 0.62, 0.7),
        ];
        for (int side = 0; side < 2; side++)
            for (int i = 0; i < 11; i++)
                Players.Add(new Player
                {
                    Side = side, Index = i, Name = Teams[side].Players[i], Role = shape[i].R, Home = new Vec(shape[i].A, shape[i].D),
                    Speed = 128 + 18 * Teams[side].Skill,
                });
        Kickoff(0);
    }

    /// <summary>Lets a side's level be set apart from its team (the cup's computer grows harder).</summary>
    public void SetSkill(int side, double skill) => Skill[side] = Math.Clamp(skill, 0, 1);

    // ───────────────────────────── directions ─────────────────────────────

    /// <summary>The way a side attacks: −1 up the screen (toward y = 0), +1 down. Sides swap at half time; in a shoot-out everyone shoots at the top goal.</summary>
    public int Dir(int side) => Shootout ? -1 : (side == 0) == (Half == 1) ? -1 : 1;

    public double AttackGoalY(int side) => Dir(side) < 0 ? 0 : Pitch.Length;

    public double OwnGoalY(int side) => Shootout ? 0 : Dir(side) < 0 ? Pitch.Length : 0;

    /// <summary>A place given across (0 to 1, left to right when attacking up) and depth (0 own goal line to 1 the other).</summary>
    public Vec Place(int side, double across, double depth) => Dir(side) < 0
        ? new Vec(across * Pitch.Width, Pitch.Length - depth * Pitch.Length)
        : new Vec((1 - across) * Pitch.Width, depth * Pitch.Length);

    public double DepthOf(int side, Vec p) => Dir(side) < 0 ? 1 - p.Y / Pitch.Length : p.Y / Pitch.Length;
    public double AcrossOf(int side, Vec p) => Dir(side) < 0 ? p.X / Pitch.Width : 1 - p.X / Pitch.Width;

    public IEnumerable<Player> Side(int side) => Players.Where(p => p.Side == side);
    public Player Keeper(int side) => Players.First(p => p.Side == side && p.IsKeeper);

    /// <summary>The side with the ball, or that touched it last.</summary>
    public int InPossession => Ball.Owner?.Side ?? Ball.LastTouch?.Side ?? KickoffSide;

    // ───────────────────────────── the step ─────────────────────────────

    public void Tick(Controls c0 = default, Controls c1 = default)
    {
        double dt = Step;
        PhaseTime += dt;
        foreach (var p in Players) p.StateTime += dt;

        switch (Phase)
        {
            case MatchPhase.HalfOver:
            case MatchPhase.Over:
                foreach (var p in Players) { p.Vel *= 0.9; p.Pos += p.Vel * dt; }
                BallPhysics(dt);
                return;
            case MatchPhase.Goal:
                Celebrate(dt);
                BallPhysics(dt);
                if (Shootout) ShootoutPause();
                else if (PhaseTime > 3.2) Kickoff(1 - _lastGoalSide);
                return;
        }

        if (Phase != MatchPhase.Stoppage && !Shootout) Clock += dt;
        ChooseControlled();
        for (int side = 0; side < 2; side++)
        {
            if (Controlled[side] is { } me) HumanStep(me, side == 0 ? c0 : c1, dt);
        }
        foreach (var p in Players)
            if (Controlled[p.Side] != p) Think(p, dt);
        foreach (var p in Players) Move(p, dt);

        if (Phase == MatchPhase.Restart) RestartStep(dt);
        BallStep(dt);
        if (Phase == MatchPhase.Live) { Contacts(); CheckLines(); }
        if (Shootout) ShootoutStep();
        if (Phase == MatchPhase.Stoppage && PhaseTime > 1.1 && _pending is { } p0)
        {
            _pending = null;
            BeginRestart(p0.Type, p0.Side, p0.Spot);
        }

        if (!Shootout && Clock >= HalfLength && Phase is MatchPhase.Live or MatchPhase.Stoppage or MatchPhase.Restart &&
            !(Phase == MatchPhase.Restart && RestartType == Restart.Penalty) && !(_pending is { Type: Restart.Penalty }))
            EndHalf();
    }

    private void Celebrate(double dt)
    {
        foreach (var p in Players)
        {
            if (p == Scorer && p.State == PState.Celebrate)
            {
                // A run off toward the corner flag, arms up.
                var corner = new Vec(p.Pos.X < Pitch.Width / 2 ? 30 : Pitch.Width - 30, p.Pos.Y < Pitch.Length / 2 ? 40 : Pitch.Length - 40);
                p.Vel = (corner - p.Pos).Norm * Math.Min(p.Speed * 1.1, (corner - p.Pos).Length * 3);
            }
            else if (p.Side == Scorer?.Side && !p.IsKeeper)
                p.Vel = ((Scorer.Pos - p.Pos).Length > 30 ? (Scorer.Pos - p.Pos).Norm * p.Speed * 0.8 : Vec.Zero);
            else p.Vel *= 0.9;
            if (p.State is PState.Slide or PState.Down or PState.Dive && p.StateTime > 0.8) SetState(p, PState.Run);
            p.Pos += p.Vel * dt;
            if (p.Vel.Length > 5) { p.Facing = p.Vel.Norm; p.Anim += p.Vel.Length * dt * 0.09; }
        }
    }

    private static void SetState(Player p, PState s)
    {
        p.State = s;
        p.StateTime = 0;
    }

    // ───────────────────────────── a person at the controls ─────────────────────────────

    /// <summary>The person runs whoever has the ball, or the outfield player nearest it (not switching for a small difference).</summary>
    private void ChooseControlled()
    {
        for (int side = 0; side < 2; side++)
        {
            if (!Human[side]) { Controlled[side] = null; continue; }
            if (Phase == MatchPhase.Restart && Taker is { } t && t.Side == side && !t.IsKeeper) { Controlled[side] = t; continue; }
            if (Ball.Owner is { IsKeeper: false } o && o.Side == side) { Controlled[side] = o; continue; }
            var cur = Controlled[side];
            if (_human[side].Charging && cur != null) continue;
            if (cur is { State: PState.Slide or PState.Down }) continue;
            var aim = Ball.Pos + Ball.Vel * 0.25;
            var best = Side(side).Where(p => !p.IsKeeper && p.State is PState.Run or PState.Celebrate).MinBy(p => p.Pos.DistanceTo(aim));
            if (best == null) continue;
            if (cur == null || cur.IsKeeper || best.Pos.DistanceTo(aim) + 14 < cur.Pos.DistanceTo(aim)) Controlled[side] = best;
        }
    }

    private void HumanStep(Player me, Controls c, double dt)
    {
        var h = _human[me.Side];
        bool pressed = c.Kick && !h.Prev, released = !c.Kick && h.Prev;
        h.Prev = c.Kick;
        var stick = c.Direction;
        bool taking = Phase == MatchPhase.Restart && Taker == me;

        // Aftertouch: the stick bends the ball just kicked, sideways for curl, back for lift, forward to keep it down.
        if (h.After > 0 && Ball.Kicker == me && Ball.Loose)
        {
            h.After -= dt;
            if (stick != Vec.Zero)
            {
                double side = stick.Dot(h.KickDir.Perp), along = stick.Dot(h.KickDir);
                Ball.Spin += h.KickDir.Perp * (side * 520 * dt);
                if (along < -0.5) Ball.Vz += 280 * dt;
                else if (along > 0.5 && Ball.Z > 0) Ball.Vz -= 220 * dt;
            }
        }

        if (taking)
        {
            me.Vel = Vec.Zero;
            if (stick != Vec.Zero && (RestartType != Restart.ThrowIn || stick.Dot(Inward(Ball.Pos)) > -0.1)) me.Facing = stick;
            me.Pos = Ball.Pos - me.Facing * 9;
        }
        else if (me.Free && stick == Vec.Zero && Ball.Loose && Ball.Target == me)
        {
            // A pass to him: left alone, he goes to meet it.
            var to = Intercept(me) - me.Pos;
            me.Vel = to.Length < 3 ? Vec.Zero : to.Norm * me.Speed * Math.Min(1, to.Length / 18);
            if (to.Length > 3) me.Facing = to.Norm;
        }
        else if (me.Free)
        {
            me.Vel = stick * me.Speed * (Ball.Owner == me ? 0.93 : 1);
            if (stick != Vec.Zero) me.Facing = stick;
        }

        bool hasBall = Ball.Owner == me || taking;
        if (hasBall && (Phase is MatchPhase.Live or MatchPhase.Restart))
        {
            if (taking && PhaseTime < 0.5) { h.Charging = false; return; }
            if (pressed) { h.Charging = true; h.Charge = 0; }
            if (!h.Charging) return;
            h.Charge += dt;
            if (!released && h.Charge < FullCharge) return;
            h.Charging = false;
            if (h.Charge < TapTime) HumanPass(me);
            else HumanShoot(me, Math.Clamp((h.Charge - TapTime) / (FullCharge - TapTime), 0, 1));
            h.After = Aftertouch;
            h.KickDir = Ball.Vel.Norm;
            return;
        }

        h.Charging = false;
        if (!pressed || Phase != MatchPhase.Live || !me.Free) return;
        double d = me.Pos.DistanceTo(Ball.Pos);
        if (Ball.Loose && d < 22 && Ball.Z is > 7 and < 36) Header(me, me.Facing);
        else if (Ball.Loose && d < 15 && Ball.Z <= 7) { Strike(me, me.Facing * 470, 30, Sfx.KickHard); h.After = Aftertouch; h.KickDir = me.Facing; }
        else StartSlide(me);
    }

    private void HumanPass(Player me)
    {
        if (Phase == MatchPhase.Restart && RestartType == Restart.ThrowIn)
        {
            var mate = BestTarget(me, me.Facing, 0.55, 260);
            if (mate != null) Lob(me, mate.Pos, Sfx.Throw, mate);
            else Strike(me, me.Facing * 170, 120, Sfx.Throw);
            return;
        }
        var target = BestTarget(me, me.Facing, 0.6, 460);
        if (target == null) { Strike(me, me.Facing * 330, 0, Sfx.Kick); return; }
        // To his feet: he comes to meet it.
        if (me.Pos.DistanceTo(target.Pos) > 330) Lob(me, target.Pos, Sfx.Kick, target);
        else Pass(me, target.Pos, target);
    }

    private void HumanShoot(Player me, double power)
    {
        if (Phase == MatchPhase.Restart && RestartType == Restart.ThrowIn)
        {
            Strike(me, me.Facing * (230 + 120 * power), 130, Sfx.Throw);
            return;
        }
        Strike(me, me.Facing * (360 + 340 * power), 18 + 110 * power * power, Sfx.KickHard);
    }

    // ───────────────────────────── kicking ─────────────────────────────

    /// <summary>The ball leaves the player's foot (or head, or hands) with this velocity.</summary>
    public void Strike(Player p, Vec vel, double vz, Sfx sound)
    {
        var b = Ball;
        if (b.Owner == p && p.State == PState.Hold) SetState(p, PState.Run);
        b.Owner = null;
        b.Vel = vel;
        b.Vz = vz;
        if (vz > 0) b.Z = Math.Max(b.Z, 0.5);
        b.Spin = Vec.Zero;
        b.LastTouch = b.Kicker = p;
        b.Target = null;
        b.SinceKick = 0;
        if (vel.Length > 1) p.Facing = vel.Norm;
        Heard.Add(sound);
        if (Phase == MatchPhase.Restart) { Phase = MatchPhase.Live; PhaseTime = 0; Taker = null; }
    }

    /// <summary>Rolling friction: speed falls as e^(−Friction·t), so a ball starting at v rolls v/Friction.</summary>
    public const double Friction = 1.05;

    /// <summary>A pass along the ground that arrives with a little pace left.</summary>
    public void Pass(Player p, Vec target, Player? to = null)
    {
        double d = Ball.Pos.DistanceTo(target);
        double v = Math.Clamp(Friction * d + 80, 150, 560);
        Strike(p, (target - Ball.Pos).Norm * v, 0, Sfx.Kick);
        Ball.Target = to;
    }

    /// <summary>A ball in the air that comes down on <paramref name="target"/>.</summary>
    public void Lob(Player p, Vec target, Sfx sound = Sfx.Kick, Player? to = null)
    {
        double d = Ball.Pos.DistanceTo(target);
        double t = Math.Clamp(d / 380, 0.45, 1.5);
        Strike(p, (target - Ball.Pos).Norm * (d / t), Pitch.Gravity * t / 2, sound);
        Ball.Target = to;
    }

    public void Header(Player p, Vec dir)
    {
        double vz = Ball.Z > 18 ? -30 : 50;
        Strike(p, dir.Norm * 330, vz, Sfx.Header);
    }

    public void StartSlide(Player p)
    {
        SetState(p, PState.Slide);
        p.Vel = p.Facing * 255;
        p.SlideTouched = false;
        if (Ball.Owner == p) Ball.Owner = null;
    }

    /// <summary>The team-mate the ball is best played to, within a cone about <paramref name="dir"/> (cosine at least <paramref name="cone"/>).</summary>
    public Player? BestTarget(Player p, Vec dir, double cone, double maxRange)
    {
        Player? best = null;
        double bestScore = double.MinValue;
        foreach (var m in Side(p.Side))
        {
            if (m == p || m.IsKeeper && Dir(p.Side) != 0 && DepthOf(p.Side, Ball.Pos) > 0.35) continue;
            var to = m.Pos - Ball.Pos;
            double d = to.Length;
            if (d < 40 || d > maxRange) continue;
            if (dir != Vec.Zero && to.Norm.Dot(dir) < cone) continue;
            double score = (DepthOf(p.Side, m.Pos) - DepthOf(p.Side, Ball.Pos)) * 260 + Math.Min(70, Openness(p.Side, Ball.Pos, m.Pos)) * 1.4 - Math.Abs(d - 190) * 0.12;
            if (dir != Vec.Zero) score += to.Norm.Dot(dir) * 60;
            if (score > bestScore) { bestScore = score; best = m; }
        }
        return best;
    }

    /// <summary>How far the nearest opponent is from the line of a pass: room to play it.</summary>
    public double Openness(int side, Vec from, Vec to)
    {
        double best = 999;
        var seg = to - from;
        double len2 = Math.Max(1, seg.Dot(seg));
        foreach (var o in Side(1 - side))
        {
            double t = Math.Clamp((o.Pos - from).Dot(seg) / len2, 0, 1);
            best = Math.Min(best, o.Pos.DistanceTo(from + seg * t));
        }
        return best;
    }

    // ───────────────────────────── moving ─────────────────────────────

    private void Move(Player p, double dt)
    {
        switch (p.State)
        {
            case PState.Slide:
                if (p.StateTime < 0.45) p.Vel *= Math.Exp(-3.2 * dt);
                else p.Vel = Vec.Zero;
                if (p.StateTime > 0.85) SetState(p, PState.Run);
                break;
            case PState.Down:
                p.Vel *= 0.85;
                if (p.StateTime > 1.0) SetState(p, PState.Run);
                break;
            case PState.Dive:
                p.Vel *= Math.Exp(-1.5 * dt);
                if (p.StateTime > 0.5) p.Vel = Vec.Zero;
                if (p.StateTime > 1.0) SetState(p, PState.Run);
                break;
            case PState.Hold:
                p.Vel = Vec.Zero;
                break;
            case PState.Celebrate:
                SetState(p, PState.Run);
                break;
        }
        p.Pos += p.Vel * dt;
        p.Pos = new Vec(Math.Clamp(p.Pos.X, -Pitch.Margin + 10, Pitch.Width + Pitch.Margin - 10), Math.Clamp(p.Pos.Y, -Pitch.Margin + 10, Pitch.Length + Pitch.Margin - 10));
        if (p.State == PState.Run && p.Vel.Length > 4) p.Anim += p.Vel.Length * dt * 0.09;
    }

    // ───────────────────────────── the ball ─────────────────────────────

    private void BallStep(double dt)
    {
        var b = Ball;
        if (b.Owner is { } o)
        {
            b.Owned += dt;
            if (o.State == PState.Hold) { b.Pos = o.Pos + o.Facing * 5; b.Z = 12; b.Vz = 0; b.Vel = Vec.Zero; return; }
            if (o.State != PState.Run) { b.Owner = null; return; }
            // Dribbling: the ball runs a step ahead of the feet.
            var want = o.Pos + o.Facing * 9;
            b.Pos += (want - b.Pos) * 0.45;
            b.Vel = o.Vel;
            b.Z = 0;
            b.Vz = 0;
            b.Spin = Vec.Zero;
            return;
        }
        BallPhysics(dt);
    }

    private void BallPhysics(double dt)
    {
        var b = Ball;
        if (b.Owner != null) return;
        b.SinceKick += dt;
        var before = b.Pos;
        b.Pos += b.Vel * dt;
        if (b.Spin != Vec.Zero)
        {
            b.Vel += b.Spin * dt;
            b.Spin *= Math.Exp(-(b.Z > 0 ? 1.6 : 6) * dt);
        }
        if (b.Z > 0 || b.Vz > 0)
        {
            b.Vz -= Pitch.Gravity * dt;
            b.Z += b.Vz * dt;
            b.Vel *= Math.Exp(-0.12 * dt);
            if (b.Z <= 0)
            {
                b.Z = 0;
                if (b.Vz < -55)
                {
                    if (b.Vz < -90) Heard.Add(Sfx.Bounce);
                    b.Vz = -b.Vz * 0.45;
                    b.Vel *= 0.82;
                }
                else b.Vz = 0;
            }
        }
        else
        {
            b.Vel *= Math.Exp(-Friction * dt);
            if (b.Vel.Length < 4) b.Vel = Vec.Zero;
        }
        Woodwork(before);
        // The advertising boards stop a ball that's gone out.
        double m = Pitch.Margin - 6;
        if (b.Pos.X < -m || b.Pos.X > Pitch.Width + m || b.Pos.Y < -m || b.Pos.Y > Pitch.Length + m)
        {
            b.Pos = new Vec(Math.Clamp(b.Pos.X, -m, Pitch.Width + m), Math.Clamp(b.Pos.Y, -m, Pitch.Length + m));
            b.Vel = Vec.Zero;
            b.Spin = Vec.Zero;
        }
    }

    /// <summary>Posts and crossbar: the ball bounces off them; past the line inside the net it slows and stops.</summary>
    private void Woodwork(Vec before)
    {
        var b = Ball;
        foreach (double gy in new[] { 0.0, Pitch.Length })
        {
            if (b.Z < Pitch.Crossbar + 2)
                foreach (double px in new[] { Pitch.Width / 2 - Pitch.GoalHalf, Pitch.Width / 2 + Pitch.GoalHalf })
                {
                    var post = new Vec(px, gy);
                    var off = b.Pos - post;
                    if (off.Length < 5 && b.Vel.Dot(off) < 0)
                    {
                        var n = off.Norm;
                        b.Vel = (b.Vel - n * (2 * b.Vel.Dot(n))) * 0.6;
                        b.Pos = post + n * 5;
                        Heard.Add(Sfx.Post);
                    }
                }

            bool crossed = (before.Y - gy) * (b.Pos.Y - gy) <= 0 && before.Y != gy;
            if (crossed && Math.Abs(b.Pos.X - Pitch.Width / 2) < Pitch.GoalHalf && b.Z > Pitch.Crossbar - 4 && b.Z < Pitch.Crossbar + 3)
            {
                b.Vel = new Vec(b.Vel.X, -b.Vel.Y * 0.5);
                b.Vz = -Math.Abs(b.Vz) * 0.5;
                b.Pos = new Vec(b.Pos.X, before.Y);
                Heard.Add(Sfx.Post);
            }

            // In the net: the netting soaks up the pace.
            bool inNet = Math.Abs(b.Pos.X - Pitch.Width / 2) < Pitch.GoalHalf && (gy == 0 ? b.Pos.Y < 0 : b.Pos.Y > Pitch.Length) && b.Z < Pitch.Crossbar;
            if (inNet && Phase is MatchPhase.Goal)
            {
                b.Vel *= 0.8;
                b.Spin = Vec.Zero;
                double back = gy == 0 ? -Pitch.NetDepth + 3 : Pitch.Length + Pitch.NetDepth - 3;
                if (gy == 0 ? b.Pos.Y < back : b.Pos.Y > back) b.Pos = new Vec(b.Pos.X, back);
            }
        }
    }

    // ───────────────────────────── touching the ball ─────────────────────────────

    private void Contacts()
    {
        var b = Ball;
        // Sliding tackles: the ball first is fair, the player first is a foul.
        foreach (var p in Players)
        {
            if (p.State != PState.Slide || p.StateTime > 0.45) continue;
            if (!p.SlideTouched && b.Z < 8 && p.Pos.DistanceTo(b.Pos) < 13 && b.Owner?.Side != p.Side)
            {
                b.Owner = null;
                p.SlideTouched = true;
                Strike(p, p.Vel.Norm * 200 + p.Vel.Perp.Norm * ((_random.NextDouble() - 0.5) * 120), 10, Sfx.Tackle);
            }
            foreach (var v in Side(1 - p.Side))
            {
                if (v.State is PState.Down or PState.Dive or PState.Hold || v.Pos.DistanceTo(p.Pos) > 12) continue;
                if (!p.SlideTouched) { Foul(p, v); return; }
                SetState(v, PState.Down);
            }
        }

        if (b.Owner is { } owner)
        {
            if (owner.State == PState.Hold)
            {
                // Nobody crowds a keeper with the ball in his hands.
                foreach (var o in Side(1 - owner.Side))
                {
                    var off = o.Pos - owner.Pos;
                    if (off.Length < 45) o.Pos = owner.Pos + (off.Length < 1 ? new Vec(0, Dir(owner.Side)) : off.Norm) * 45;
                }
                return;
            }
            // A standing tackle: an opponent at the ball wins it, sooner or later.
            if (b.Owned < 0.4) return;
            foreach (var o in Side(1 - owner.Side))
            {
                if (!o.Free || o.Pos.DistanceTo(b.Pos) > 9) continue;
                double chance = Controlled[o.Side] == o ? 0.09 : 0.03 + 0.06 * Skill[o.Side];
                if (_random.NextDouble() < chance)
                {
                    Heard.Add(Sfx.Tackle);
                    if (_random.NextDouble() < 0.5) { Take(o); return; }
                    // Poked away: loose, somewhere off the two of them.
                    var away = (owner.Facing * -0.3 + new Vec(_random.NextDouble() - 0.5, _random.NextDouble() - 0.5) * 2).Norm;
                    b.Owner = null;
                    b.Vel = away * (120 + 140 * _random.NextDouble());
                    b.LastTouch = b.Kicker = o;
                    b.SinceKick = 0;
                    return;
                }
            }
            return;
        }

        // A loose ball: a keeper in his area may catch it; anyone else can trap it, or it glances off them.
        Player? nearest = null;
        double nd = double.MaxValue;
        foreach (var p in Players)
        {
            if (p.State is not (PState.Run or PState.Dive)) continue;
            if (p == b.Kicker && b.SinceKick < 0.3) continue;
            double d = p.Pos.DistanceTo(b.Pos);
            bool backPass = b.LastTouch?.Side == p.Side && b.LastTouch != p;
            if (p.IsKeeper && !backPass && Pitch.InBox(p.Pos, OwnGoalY(p.Side), 4) && b.Z < 32 && d < (p.State == PState.Dive ? 18 : 12))
            {
                KeeperTouch(p);
                return;
            }
            if (p.State == PState.Run && b.Z < 9 && d < 10 && d < nd) { nearest = p; nd = d; }
            else if (p.State == PState.Run && b.Z is >= 9 and < 24 && d < 8 && b.Vel.Length > 120 && b.Kicker?.Side != p.Side)
            {
                // A ball at chest height is knocked down.
                b.Vel *= 0.25;
                b.Vz = 0;
                b.LastTouch = p;
            }
        }
        if (nearest is not { } n) return;
        double speed = (b.Vel - n.Vel).Length;
        if (speed < 390) Take(n);
        else
        {
            b.Vel = b.Vel * -0.25 + n.Facing * 40;
            b.LastTouch = n;
            b.Kicker = n;
            b.SinceKick = 0;
        }
    }

    private void Take(Player p)
    {
        Ball.Target = null;
        Ball.Owner = p;
        Ball.Owned = 0;
        Ball.LastTouch = p;
        Ball.Spin = Vec.Zero;
        p.Think = 0.1 + _random.NextDouble() * 0.2;
        if (Phase == MatchPhase.Restart) { Phase = MatchPhase.Live; PhaseTime = 0; }
    }

    /// <summary>Catches what it can hold; a shot too hard to hold is pushed away.</summary>
    private void KeeperTouch(Player k)
    {
        var b = Ball;
        double speed = b.Vel.Length, holdable = 300 + 220 * Skill[k.Side];
        if (speed < holdable || _random.NextDouble() < 0.25 * Skill[k.Side])
        {
            b.Owner = k;
            b.Owned = 0;
            b.LastTouch = k;
            b.Vel = Vec.Zero;
            b.Spin = Vec.Zero;
            k.Vel = Vec.Zero;
            k.Facing = new Vec(0, Dir(k.Side));
            SetState(k, PState.Hold);
            Heard.Add(Sfx.Catch);
            return;
        }
        // Parried: out wide, or round the post for a corner.
        double side = b.Pos.X < Pitch.Width / 2 ? -1 : 1;
        bool round = _random.NextDouble() < 0.4;
        double forward = round ? -Dir(k.Side) * Math.Max(80, Math.Abs(b.Vel.Y) * 0.3) : Dir(k.Side) * Math.Max(60, Math.Abs(b.Vel.Y) * 0.35);
        b.Vel = new Vec(side * (90 + speed * (round ? 0.35 : 0.25)), forward);
        b.Vz = 70;
        b.Z = Math.Max(b.Z, 1);
        b.LastTouch = b.Kicker = k;
        b.SinceKick = 0;
        Heard.Add(Sfx.Save);
    }

    private void Foul(Player by, Player victim)
    {
        SetState(victim, PState.Down);
        Heard.Add(Sfx.Whistle);
        Events.Add(new MatchEvent("foul", victim.Side, $"FOUL BY {by.Name}"));
        bool penalty = Pitch.InBox(victim.Pos, OwnGoalY(by.Side)) && Pitch.InPlay(victim.Pos);
        var spot = new Vec(Math.Clamp(victim.Pos.X, 4, Pitch.Width - 4), Math.Clamp(victim.Pos.Y, 4, Pitch.Length - 4));
        if (penalty) spot = new Vec(Pitch.Width / 2, Math.Abs(OwnGoalY(by.Side) - Pitch.PenaltySpot));
        Stop(penalty ? Restart.Penalty : Restart.FreeKick, victim.Side, spot);
    }

    private void Stop(Restart type, int side, Vec spot)
    {
        if (Ball.Owner != null) Ball.Owner = null;
        _pending = (type, side, spot);
        Phase = MatchPhase.Stoppage;
        PhaseTime = 0;
    }

    /// <summary>Over a line: a goal, a corner, a goal kick or a throw-in.</summary>
    private void CheckLines()
    {
        var b = Ball;
        var at = b.Pos;
        int last = b.LastTouch?.Side ?? 0;
        if (at.Y < 0 || at.Y > Pitch.Length)
        {
            double gy = at.Y < 0 ? 0 : Pitch.Length;
            // The side defending this goal.
            int defending = OwnGoalY(0) == gy ? 0 : 1;
            if (Shootout) defending = 1 - ShootSide;
            if (Math.Abs(at.X - Pitch.Width / 2) < Pitch.GoalHalf && b.Z < Pitch.Crossbar)
            {
                ScoreGoal(1 - defending);
                return;
            }
            b.Owner = null;
            Heard.Add(Sfx.Whistle);
            if (Shootout) { Stop(Restart.Penalty, ShootSide, Vec.Zero); return; }
            if (last == defending)
            {
                var corner = new Vec(at.X < Pitch.Width / 2 ? 2 : Pitch.Width - 2, gy == 0 ? 2 : Pitch.Length - 2);
                Stop(Restart.Corner, 1 - defending, corner);
            }
            else
            {
                var spot = new Vec(Pitch.Width / 2 + (at.X < Pitch.Width / 2 ? -60 : 60), gy == 0 ? Pitch.SixDepth - 6 : Pitch.Length - Pitch.SixDepth + 6);
                Stop(Restart.GoalKick, defending, spot);
            }
            return;
        }
        if (at.X < 0 || at.X > Pitch.Width)
        {
            b.Owner = null;
            Heard.Add(Sfx.Whistle);
            if (Shootout) { Stop(Restart.Penalty, ShootSide, Vec.Zero); return; }
            Stop(Restart.ThrowIn, 1 - last, new Vec(at.X < 0 ? 0 : Pitch.Width, Math.Clamp(at.Y, 6, Pitch.Length - 6)));
        }
    }

    private void ScoreGoal(int side)
    {
        var scorer = Ball.LastTouch;
        bool own = scorer != null && scorer.Side != side;
        Scorer = own ? null : scorer;
        Ball.Owner = null;
        Heard.Add(Sfx.Goal);
        Phase = MatchPhase.Goal;
        PhaseTime = 0;
        if (Shootout)
        {
            ShootResult(true);
            return;
        }
        Score[side]++;
        _lastGoalSide = side;
        Events.Add(new MatchEvent("goal", side, own ? $"OWN GOAL BY {scorer!.Name}" : $"GOAL! {scorer?.Name ?? ""}"));
        if (Scorer != null) SetState(Scorer, PState.Celebrate);
    }

    private void EndHalf()
    {
        Heard.Add(Sfx.WhistleLong);
        Phase = Half == 1 ? MatchPhase.HalfOver : MatchPhase.Over;
        PhaseTime = 0;
        Ball.Owner = null;
        _pending = null;
        Taker = null;
        Events.Add(new MatchEvent(Half == 1 ? "half" : "full", 0, Half == 1 ? "HALF TIME" : "FULL TIME"));
    }

    /// <summary>The second half: ends swapped, the other side kicks off.</summary>
    public void StartSecondHalf()
    {
        Half = 2;
        Clock = 0;
        Kickoff(1);
    }

    // ───────────────────────────── restarts ─────────────────────────────

    public void Kickoff(int side)
    {
        KickoffSide = side;
        foreach (var p in Players)
        {
            SetState(p, PState.Run);
            p.Vel = Vec.Zero;
            double depth = p.IsKeeper ? 0.02 : Math.Min(p.Home.Y * 0.62, p.Side == side ? 0.47 : 0.4);
            p.Pos = Place(p.Side, p.Home.X, depth);
            p.Facing = new Vec(0, Dir(p.Side));
        }
        var fwds = Side(side).Where(p => p.Role == Role.Forward).ToList();
        var centre = Pitch.Centre;
        fwds[0].Pos = centre - new Vec(0, Dir(side)) * 10;
        fwds[1].Pos = centre + new Vec(50, -Dir(side) * 14);
        Scorer = null;
        BeginRestart(Restart.Kickoff, side, centre, taker: fwds[0]);
        Heard.Add(Sfx.Whistle);
    }

    private void BeginRestart(Restart type, int side, Vec spot, Player? taker = null)
    {
        if (Shootout && type == Restart.Penalty && spot == Vec.Zero) { ShootResult(false); return; }
        var b = Ball;
        b.Owner = null;
        b.Pos = spot;
        b.Vel = Vec.Zero;
        b.Spin = Vec.Zero;
        b.Z = 0;
        b.Vz = 0;
        RestartType = type;
        RestartSide = side;
        Phase = MatchPhase.Restart;
        PhaseTime = 0;
        _human[0].Charging = _human[1].Charging = false;
        var aim = (new Vec(Pitch.Width / 2, AttackGoalY(side)) - spot).Norm;
        if (type == Restart.ThrowIn) aim = (Inward(spot) + new Vec(0, Dir(side)) * 0.6).Norm;
        if (type == Restart.GoalKick) aim = new Vec(0, Dir(side));
        taker ??= type == Restart.GoalKick ? Keeper(side)
            : type == Restart.Penalty ? Side(side).Where(p => p.Role == Role.Forward).OrderBy(p => p.Index).Last()
            : Side(side).Where(p => !p.IsKeeper && p.State != PState.Down).MinBy(p => p.Pos.DistanceTo(spot))!;
        Taker = taker;
        SetState(taker, PState.Run);
        taker.Facing = aim;
        taker.Pos = spot - aim * 9;
        taker.Vel = Vec.Zero;
        if (type == Restart.Penalty) ClearTheBox(side, spot);
    }

    /// <summary>For a penalty everyone but the taker and the keeper stands outside the area.</summary>
    private void ClearTheBox(int side, Vec spot)
    {
        double gy = Shootout ? 0 : AttackGoalY(side);
        int i = 0;
        foreach (var p in Players)
        {
            if (p == Taker) continue;
            SetState(p, PState.Run);
            p.Vel = Vec.Zero;
            if (p.IsKeeper && p.Side != side) { p.Pos = new Vec(Pitch.Width / 2, gy + (gy == 0 ? 4 : -4)); p.Facing = new Vec(0, gy == 0 ? 1 : -1); continue; }
            if (Shootout) { p.Pos = Pitch.Centre + new Vec((i++ % 10 - 4.5) * 22, p.Side * 26 - 13); continue; }
            double line = gy == 0 ? Pitch.BoxDepth + 24 : Pitch.Length - Pitch.BoxDepth - 24;
            p.Pos = new Vec(Pitch.Width / 2 + (i++ % 18 - 8.5) * 26, line + (gy == 0 ? 1 : -1) * (i % 2) * 18);
        }
        _ = spot;
    }

    /// <summary>The direction into the pitch from a point on (or near) the touchline.</summary>
    public static Vec Inward(Vec p) => new(p.X < Pitch.Width / 2 ? 1 : -1, 0);

    private void RestartStep(double dt)
    {
        var t = Taker;
        if (t == null) return;
        // The other side stands off the ball.
        foreach (var p in Side(1 - RestartSide))
        {
            var off = p.Pos - Ball.Pos;
            if (off.Length < 70 && RestartType != Restart.Penalty) p.Pos = Ball.Pos + off.Norm * 70;
        }
        if (Controlled[t.Side] == t) { if (PhaseTime > 12) HumanPass(t); return; }
        t.Vel = Vec.Zero;
        t.Pos = Ball.Pos - t.Facing * 9;
        double wait = RestartType switch { Restart.Kickoff => 0.9, Restart.Penalty => 1.3, Restart.GoalKick => 1.2, _ => 1.0 };
        if (PhaseTime < wait) return;
        ComputerRestart(t);
    }

    // ───────────────────────────── the shoot-out ─────────────────────────────

    public bool Shootout { get; private set; }
    public List<bool>[] Penalties { get; } = [[], []];
    public int ShootSide { get; private set; }
    public int? ShootWinner { get; private set; }
    private bool _kickResolved;

    public int PenaltyGoals(int side) => Penalties[side].Count(x => x);

    /// <summary>Five penalties each at the top goal, then one each until someone misses.</summary>
    public void StartShootout()
    {
        Shootout = true;
        ShootSide = 0;
        NextPenalty();
    }

    private void NextPenalty()
    {
        _kickResolved = false;
        BeginRestart(Restart.Penalty, ShootSide, new Vec(Pitch.Width / 2, Pitch.PenaltySpot),
            Side(ShootSide).Where(p => !p.IsKeeper).OrderByDescending(p => p.Index).ElementAt(Penalties[ShootSide].Count % 10));
    }

    private void ShootoutStep()
    {
        if (Phase != MatchPhase.Live || _kickResolved) return;
        var b = Ball;
        bool held = b.Owner is { IsKeeper: true };
        bool dead = b.Loose && b.Z <= 0 && b.Vel.Length < 15;
        if (held || dead || PhaseTime > 3.5) ShootResult(false);
    }

    private void ShootResult(bool scored)
    {
        if (_kickResolved) return;
        _kickResolved = true;
        Penalties[ShootSide].Add(scored);
        if (!scored) Heard.Add(Sfx.Miss);
        Events.Add(new MatchEvent(scored ? "pen-goal" : "pen-miss", ShootSide, scored ? "SCORED" : "MISSED"));
        int a = PenaltyGoals(0), bb = PenaltyGoals(1), ka = Penalties[0].Count, kb = Penalties[1].Count;
        bool over = ka <= 5 && kb <= 5
            ? a + (5 - ka) < bb || bb + (5 - kb) < a || ka == 5 && kb == 5 && a != bb
            : ka == kb && a != bb;
        if (over)
        {
            ShootWinner = a > bb ? 0 : 1;
            Phase = MatchPhase.Over;
            PhaseTime = 0;
            Heard.Add(Sfx.WhistleLong);
            return;
        }
        Phase = MatchPhase.Goal;
        PhaseTime = 0;
        _penaltyPause = true;
    }

    private bool _penaltyPause;

    /// <summary>Between penalties: called by the step while the result shows.</summary>
    public void ShootoutPause()
    {
        if (!_penaltyPause || Phase != MatchPhase.Goal || PhaseTime < 1.6) return;
        _penaltyPause = false;
        ShootSide = 1 - ShootSide;
        NextPenalty();
    }

    // ───────────────────────────── for tests ─────────────────────────────

    /// <summary>Puts the ball at a player's feet, in open play.</summary>
    public void Give(Player p)
    {
        Phase = MatchPhase.Live;
        PhaseTime = 0;
        Taker = null;
        _pending = null;
        Ball.Pos = p.Pos + p.Facing * 9;
        Ball.Z = Ball.Vz = 0;
        Ball.Vel = Vec.Zero;
        Take(p);
    }

    /// <summary>Open play with the ball loose at a spot.</summary>
    public void Loose(Vec at, Vec vel, double z = 0, double vz = 0)
    {
        Phase = MatchPhase.Live;
        PhaseTime = 0;
        Taker = null;
        _pending = null;
        Ball.Owner = null;
        Ball.Pos = at;
        Ball.Vel = vel;
        Ball.Z = z;
        Ball.Vz = vz;
        Ball.Spin = Vec.Zero;
        Ball.SinceKick = 1;
    }

    public void Freeze()
    {
        foreach (var p in Players) { p.Vel = Vec.Zero; SetState(p, PState.Run); }
    }

    public (Restart Type, int Side, Vec Spot)? Pending => _pending;
}
