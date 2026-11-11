namespace AVAPitchSide.Engine;

/// <summary>
/// The computer's football: every player not under a person's control. Each keeps a place in the formation that
/// shifts with the ball; the nearest one or two go for it; the one with it dribbles, passes or shoots; keepers
/// cover the angle, dive at shots and come for loose balls. A side's <see cref="Match.Skill"/> sets its reactions,
/// its aim and how often it wins the ball.
/// </summary>
public sealed partial class Match
{
    private readonly Player?[] _chasers = new Player?[4];
    private int _chaseFrame = -1;
    private int _frame;

    private double Rand(double a, double b) => a + (b - a) * _random.NextDouble();

    private double Gauss() => (_random.NextDouble() + _random.NextDouble() + _random.NextDouble() - 1.5) * 1.4;

    /// <summary>Where the ball will be soonest reachable: where it lands if it's in the air, a little ahead if it rolls.</summary>
    public Vec Intercept(Player p)
    {
        var b = Ball;
        if (b.Owner != null) return b.Pos + b.Owner.Vel * 0.25;
        if (b.Z > 1 || b.Vz > 0)
        {
            double t = (b.Vz + Math.Sqrt(b.Vz * b.Vz + 2 * Pitch.Gravity * b.Z)) / Pitch.Gravity;
            return b.Pos + b.Vel * t;
        }
        double reach = p.Pos.DistanceTo(b.Pos) / p.Speed;
        // A rolling ball slows: it goes (v/k)(1 − e^(−kt)) in time t.
        return b.Pos + b.Vel * ((1 - Math.Exp(-Friction * Math.Min(reach, 1.5))) / Friction);
    }

    /// <summary>The computer's players who go for the ball this frame: one a side, two when the other side has it.</summary>
    private bool IsChaser(Player p)
    {
        if (_chaseFrame != _frame)
        {
            _chaseFrame = _frame;
            for (int side = 0; side < 2; side++)
            {
                _chasers[side * 2] = _chasers[side * 2 + 1] = null;
                if (Human[side] || Ball.Owner is { State: PState.Hold }) continue;
                var order = Side(side).Where(q => !q.IsKeeper && q.Free).OrderBy(q => q.Pos.DistanceTo(Intercept(q))).Take(2).ToList();
                if (order.Count > 0) _chasers[side * 2] = order[0];
                if (order.Count > 1 && Ball.Owner is { } o && o.Side != side && order[1].Pos.DistanceTo(Ball.Pos) < 150) _chasers[side * 2 + 1] = order[1];
            }
        }
        return _chasers[p.Side * 2] == p || _chasers[p.Side * 2 + 1] == p;
    }

    private void Go(Player p, Vec target, double pace = 1)
    {
        var to = target - p.Pos;
        double d = to.Length;
        double speed = p.Speed * pace * Math.Min(1, d / 18);
        p.Vel = d < 2 ? Vec.Zero : to.Norm * speed;
        if (d > 3) p.Facing = to.Norm;
    }

    private void Think(Player p, double dt)
    {
        if (p.Side == 0 && p.Index == 0) _frame++;
        if (p.IsKeeper) { KeeperThink(p, dt); return; }
        if (!p.Free) return;
        if (Shootout)
        {
            if (p != Taker) p.Vel = Vec.Zero;
            return;
        }
        if (Phase is MatchPhase.Restart or MatchPhase.Stoppage)
        {
            if (p == Taker) return;
            Go(p, Position(p, InPossessionAt(p.Side)), 0.8);
            return;
        }

        var b = Ball;
        if (b.Owner == p) { OwnerThink(p, dt); return; }

        // A ball dropping by the head is headed: at goal near it, away from danger otherwise.
        if (b.Loose && b.Z is > 8 and < 34 && p.Pos.DistanceTo(b.Pos) < 15 && b.Kicker != p && b.SinceKick > 0.12)
        {
            var goal = new Vec(Pitch.Width / 2 + Rand(-30, 30), AttackGoalY(p.Side));
            bool near = p.Pos.DistanceTo(goal) < 220;
            var dir = near ? goal - p.Pos : DepthOf(p.Side, p.Pos) < 0.4 ? new Vec(Rand(-0.6, 0.6), Dir(p.Side)) : goal - p.Pos;
            Header(p, dir);
            return;
        }

        bool ours = b.Owner?.Side == p.Side;
        if (b.Loose && b.Target == p) { Go(p, Intercept(p), 1); return; }
        if (!ours && IsChaser(p))
        {
            var at = Intercept(p);
            Go(p, at, 1);
            // A slide at a dribbler in front, now and then.
            if (b.Owner is { } o && o.Side != p.Side)
            {
                var to = b.Pos - p.Pos;
                if (to.Length < 26 && to.Norm.Dot(p.Facing) > 0.75 && _random.NextDouble() < (0.006 + 0.016 * Skill[p.Side]))
                {
                    p.Facing = to.Norm;
                    StartSlide(p);
                }
            }
            return;
        }
        Go(p, Position(p, ours || b.Loose && b.LastTouch?.Side == p.Side), ours ? 0.85 : 0.75);
    }

    private bool InPossessionAt(int side) => Phase == MatchPhase.Restart ? RestartSide == side : _pending?.Side == side;

    /// <summary>Where a player should be: the formation, pulled after the ball, higher up when the side has it.</summary>
    public Vec Position(Player p, bool attacking)
    {
        double bd = DepthOf(p.Side, Ball.Pos), ba = AcrossOf(p.Side, Ball.Pos);
        double depth = p.Home.Y * 0.75 + bd * 0.45 - 0.12 + (attacking ? 0.1 : -0.02);
        // Forwards drift about, making themselves available.
        if (attacking && p.Role == Role.Forward) depth += 0.05 * Math.Sin(_frame * 0.01 + p.Index);
        (double lo, double hi) = p.Role switch
        {
            Role.Defender => (0.06, 0.58),
            Role.Midfielder => (0.18, 0.8),
            _ => (0.34, 0.9),
        };
        depth = Math.Clamp(depth, lo, hi);
        // Out of possession, defenders stay goal-side of the ball.
        if (!attacking && p.Role == Role.Defender) depth = Math.Min(depth, Math.Max(lo, bd - 0.05));
        double across = Math.Clamp(p.Home.X + (ba - 0.5) * 0.4, 0.05, 0.95);
        return Place(p.Side, across, depth);
    }

    private void OwnerThink(Player p, double dt)
    {
        var goal = new Vec(Pitch.Width / 2, AttackGoalY(p.Side));
        var toGoal = goal - p.Pos;
        double dist = toGoal.Length, skill = Skill[p.Side];
        p.Think -= dt;
        if (p.Think <= 0)
        {
            p.Think = 0.18 + _random.NextDouble() * 0.15;
            var near = Side(1 - p.Side).Where(o => !o.IsKeeper).MinBy(o => o.Pos.DistanceTo(p.Pos));
            double pressure = near == null ? 999 : near.Pos.DistanceTo(p.Pos);
            bool inFront = near != null && (near.Pos - p.Pos).Norm.Dot(toGoal.Norm) > 0.2;
            if (dist < 200 + 70 * skill && Math.Abs(toGoal.Y) > 24) { ComputerShoot(p); return; }
            // Out wide near their area: a cross into the middle.
            if (DepthOf(p.Side, p.Pos) > 0.7 && Math.Abs(p.Pos.X - Pitch.Width / 2) > Pitch.BoxHalf - 20 && _random.NextDouble() < 0.5)
            {
                Lob(p, new Vec(Pitch.Width / 2 + Rand(-70, 70), Math.Abs(AttackGoalY(p.Side) - Rand(60, 150))));
                return;
            }
            // Under pressure deep in his own half, a defender just gets rid of it: long and wide.
            if (pressure < 34 && DepthOf(p.Side, p.Pos) < 0.3 && _random.NextDouble() < 0.6 - 0.3 * skill)
            {
                var wide = Place(p.Side, p.Pos.X < Pitch.Width / 2 == (Dir(p.Side) < 0) ? Rand(-0.05, 0.25) : Rand(0.75, 1.05), DepthOf(p.Side, p.Pos) + Rand(0.25, 0.45));
                Lob(p, wide);
                return;
            }
            if (pressure < 40 && inFront || _random.NextDouble() < 0.07 || dist > 900 && _random.NextDouble() < 0.15)
            {
                var mate = BestTarget(p, Vec.Zero, -1, 420);
                if (mate != null && (DepthOf(p.Side, mate.Pos) > DepthOf(p.Side, p.Pos) - 0.08 || pressure < 30))
                {
                    var lead = mate.Pos + mate.Vel * 0.4;
                    var miss = new Vec(Gauss(), Gauss()) * ((1 - skill) * 26 + 6);
                    if (Openness(p.Side, Ball.Pos, lead) < 18 || p.Pos.DistanceTo(lead) > 300) Lob(p, lead + miss, Sfx.Kick, mate);
                    else Pass(p, lead + miss, mate);
                    return;
                }
            }
            // Dribble for goal, stepping round whoever is in the way and away from the lines.
            var dir = toGoal.Norm;
            if (near != null && pressure < 70 && inFront)
            {
                var side = (p.Pos - near.Pos).Dot(dir.Perp) >= 0 ? dir.Perp : -dir.Perp;
                dir = (dir + side * 0.9).Norm;
            }
            if (p.Pos.X < 60) dir = (dir + new Vec(0.8, 0)).Norm;
            if (p.Pos.X > Pitch.Width - 60) dir = (dir + new Vec(-0.8, 0)).Norm;
            p.Dribble = dir;
        }
        if (p.Dribble == Vec.Zero) p.Dribble = toGoal.Norm;
        p.Vel = p.Dribble * p.Speed * 0.92;
        p.Facing = p.Dribble;
    }

    /// <summary>A shot at the far side of the goal from the keeper, as high as it means (now and then over the bar).</summary>
    private void ComputerShoot(Player p, double? aimX = null)
    {
        double skill = Skill[p.Side];
        var k = Keeper(1 - p.Side);
        double gy = AttackGoalY(p.Side);
        double x = aimX ?? Pitch.Width / 2 + (k.Pos.X < Pitch.Width / 2 ? 1 : -1) * Pitch.GoalHalf * Rand(0.45, 0.85);
        double dist = Ball.Pos.DistanceTo(new Vec(x, gy));
        x += Gauss() * ((1 - skill) * 30 + 9) * Math.Max(0.6, dist / 200);
        var aim = new Vec(x, gy);
        double speed = 430 + Math.Min(250, dist * 0.9);
        double t = dist / speed, h = Rand(2, 18 + 24 * (1 - skill));
        double vz = (h + Pitch.Gravity * t * t / 2) / t;
        Strike(p, (aim - Ball.Pos).Norm * speed, vz, Sfx.KickHard);
    }

    private void KeeperThink(Player k, double dt)
    {
        var b = Ball;
        double gy = OwnGoalY(k.Side);
        var own = new Vec(Pitch.Width / 2, gy);
        var outv = new Vec(0, gy == 0 ? 1 : -1);
        if (b.Owner == k)
        {
            if (Phase == MatchPhase.Live && k.StateTime > 1.2 && !Shootout) Distribute(k);
            return;
        }
        if (!k.Free) return;
        if (Shootout && k.Side == ShootSide) { k.Vel = Vec.Zero; return; }
        if (Phase is MatchPhase.Restart or MatchPhase.Stoppage)
        {
            if (k != Taker) Go(k, own + outv * (RestartType == Restart.Penalty || Shootout ? 4 : 30), 0.7);
            return;
        }

        // A shot coming: get across, or dive.
        if (b.Loose && b.Vel.Dot(outv) < -140 && b.Kicker?.Side != k.Side)
        {
            double t = (k.Pos.Y - b.Pos.Y) / b.Vel.Y;
            double reaction = 0.2 - 0.15 * Skill[k.Side];
            if (t is > 0 and < 1.3 && b.SinceKick > reaction)
            {
                double cross = b.Pos.X + b.Vel.X * t + 0.5 * b.Spin.X * t * t;
                // Only what's on target: a ball going wide is left to go wide.
                if (Math.Abs(cross - Pitch.Width / 2) < Pitch.GoalHalf + 4 && b.Z + b.Vz * t - Pitch.Gravity * t * t / 2 < Pitch.Crossbar + 2)
                {
                    double dx = cross - k.Pos.X;
                    if (Math.Abs(dx) > 13 && t < 0.42)
                    {
                        SetState(k, PState.Dive);
                        k.Vel = new Vec(Math.Sign(dx) * Math.Min(330, Math.Abs(dx) / Math.Max(t, 0.15) + 50), 0);
                        k.Facing = new Vec(Math.Sign(dx), 0);
                        return;
                    }
                    Go(k, new Vec(cross, k.Pos.Y), 1.1);
                    return;
                }
            }
        }

        // A loose ball in the area that he'll get to first: come and collect it.
        if (b.Loose && Pitch.InBox(b.Pos, gy, -6) && b.Vel.Length < 240 && b.Z < 22)
        {
            double mine = k.Pos.DistanceTo(b.Pos);
            bool first = Side(1 - k.Side).All(o => o.Pos.DistanceTo(b.Pos) > mine * 0.9);
            if (first) { Go(k, Intercept(k), 1); return; }
        }

        // Narrow the angle: on the line from the goal to the ball, a few strides out.
        var ball = b.Pos;
        double out_ = Math.Clamp(ball.DistanceTo(own) * 0.1, 6, 40);
        var spot = own + (ball - own).Norm * out_;
        spot = new Vec(Math.Clamp(spot.X, Pitch.Width / 2 - Pitch.GoalHalf - 10, Pitch.Width / 2 + Pitch.GoalHalf + 10), spot.Y);
        Go(k, spot, 0.9);
        k.Facing = (ball - k.Pos).Norm;
    }

    private void Distribute(Player k)
    {
        var mate = BestTarget(k, Vec.Zero, -1, 560);
        if (mate == null) { Lob(k, Place(k.Side, 0.5, 0.5)); return; }
        var to = mate.Pos + mate.Vel * 0.5;
        if (k.Pos.DistanceTo(to) > 240) Lob(k, to, Sfx.Kick, mate);
        else Pass(k, to, mate);
    }

    private void ComputerRestart(Player t)
    {
        switch (RestartType)
        {
            case Restart.Kickoff:
            {
                var mate = Side(t.Side).Where(m => m != t && !m.IsKeeper).MinBy(m => m.Pos.DistanceTo(t.Pos))!;
                Pass(t, mate.Pos);
                break;
            }
            case Restart.ThrowIn:
            {
                var mate = BestTarget(t, Vec.Zero, -1, 240);
                if (mate != null) Lob(t, mate.Pos + mate.Vel * 0.3, Sfx.Throw);
                else Strike(t, t.Facing * 170, 120, Sfx.Throw);
                break;
            }
            case Restart.Corner:
            {
                double gy = AttackGoalY(t.Side);
                if (_random.NextDouble() < 0.2)
                {
                    var mate = Side(t.Side).Where(m => m != t).MinBy(m => m.Pos.DistanceTo(t.Pos))!;
                    Pass(t, mate.Pos);
                }
                else Lob(t, new Vec(Pitch.Width / 2 + Rand(-90, 90), Math.Abs(gy - Rand(70, 130))));
                break;
            }
            case Restart.GoalKick:
                Distribute(t);
                break;
            case Restart.FreeKick:
            {
                var goal = new Vec(Pitch.Width / 2, AttackGoalY(t.Side));
                if (Ball.Pos.DistanceTo(goal) < 290) { ComputerShoot(t); break; }
                var mate = BestTarget(t, Vec.Zero, -1, 460);
                if (mate == null) Lob(t, Place(t.Side, 0.5, DepthOf(t.Side, Ball.Pos) + 0.2));
                else if (t.Pos.DistanceTo(mate.Pos) > 280) Lob(t, mate.Pos);
                else Pass(t, mate.Pos);
                break;
            }
            case Restart.Penalty:
                ComputerShoot(t, Pitch.Width / 2 + (_random.Next(2) * 2 - 1) * Pitch.GoalHalf * Rand(0.35, 0.8));
                break;
        }
    }
}
