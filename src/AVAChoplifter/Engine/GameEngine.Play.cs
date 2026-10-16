namespace AVAChoplifter.Engine;

public sealed partial class GameEngine
{
    public const double MaxSpeed = 330, Accel = 460, Climb = 220, LandSpeed = 150, TurnSeconds = 0.18;
    public const double BulletSpeed = 760, BulletLife = 0.8, FireGap = 0.18, BombGap = 0.4, BombDrop = 90, Gravity = 540;
    public const double WaitSpeed = 26, RunSpeed = 90, WalkSpeed = 70, CallRange = 520, BoardGap = 0.16, UnloadGap = 0.22, BlastRadius = 40;
    public const double JetSpeed = 430, MissileSpeed = 560, MissileLife = 1.6, ShellLife = 2.2;
    public const double StartSeconds = 2.4, DyingSeconds = 3, SafeSeconds = 2, ClearSeconds = 3.6, AlertSeconds = 25;
    public const int PerBarracks = 8, RescuePoints = 500, TankPoints = 150, JetPoints = 200, MinePoints = 100, ExtraLifeEvery = 20000;
    public const int SafeBonus = 5000, HomeBonus = 100;

    /// <summary>The sortie: 1 for the first.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Chopper Player { get; private set; } = new();
    public List<Barracks> Huts { get; } = [];
    public List<Hostage> People { get; } = [];
    public List<Tank> Tanks { get; } = [];
    public List<Jet> Jets { get; } = [];
    public List<Mine> Mines { get; } = [];
    public List<Shot> Shots { get; } = [];
    /// <summary>Where the screen's left edge is on the strip.</summary>
    public double Camera { get; private set; }
    /// <summary>The enemy roused: by the first barracks shot open, or after a while anyway.</summary>
    public bool Alert { get; private set; }
    /// <summary>This sortie's hostages: brought home, and lost.</summary>
    public int Home { get; private set; }
    public int Lost { get; private set; }
    /// <summary>Every hostage brought home this game.</summary>
    public int Rescued { get; private set; }
    public ChopperPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public int Bonus { get; private set; }
    /// <summary>For tests: the enemy stays where it's put, holds its fire, sends nobody more, and never wakes on its own.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;
    private double _tankIn, _jetIn, _mineIn, _playTime;

    public int TankLimit => Math.Min(6, 1 + Wave);
    public double TankSpeed => Math.Min(84, 56 + 4 * (Wave - 1));
    public double TankFireGap => Math.Max(1.4, 3.0 - 0.2 * (Wave - 1));
    public double ShellSpeed => Math.Min(420, 280 + 20 * (Wave - 1));
    public double JetGap => Math.Max(7, 16 - 1.5 * (Wave - 1));
    public int MineLimit => Wave < 3 ? 0 : Math.Min(4, Wave - 2);
    public double MineSpeed => Math.Min(130, 70 + 8 * (Wave - 3));

    /// <summary>Hostages not yet home or lost: in the barracks, out waiting, aboard, or walking in.</summary>
    public int Unaccounted => Huts.Sum(h => h.Inside) + People.Count(p => !p.Gone) + Player.Aboard;

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        Rescued = 0;
        _nextExtra = ExtraLifeEvery;
        Build(1);
        Restart();
        SetPhase(Phase.Start);
    }

    /// <summary>A sortie laid out: four barracks along the enemy's side of the strip, a few hostages in each. Nearly the same every time.</summary>
    private void Build(int level)
    {
        var rng = new Random(1985 + level * 409);
        Huts.Clear();
        People.Clear();
        Tanks.Clear();
        foreach (double x in new double[] { 4150, 3250, 2300, 1300 })
            Huts.Add(new Barracks { X = x + (rng.NextDouble() - 0.5) * 200, Inside = PerBarracks, Held = PerBarracks });
        Alert = false;
        Home = Lost = 0;
        _playTime = 0;
    }

    /// <summary>A fresh chopper on the pad; whoever was in the air leaves, and anyone running for the old one waits again.</summary>
    private void Restart()
    {
        Player = new Chopper { Safe = SafeSeconds };
        Shots.Clear();
        Jets.Clear();
        Mines.Clear();
        Tanks.RemoveAll(t => t.Dead || Math.Abs(t.X - Player.X) < 1400);
        foreach (var h in People.Where(h => h.State == HostageState.Running)) h.State = HostageState.Waiting;
        _tankIn = 3;
        _jetIn = JetGap * 0.6;
        _mineIn = 8;
        Camera = Math.Clamp(Player.X - Field.Width / 2 - 160, 0, Field.WorldWidth - Field.Width);
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        Build(wave);
        Restart();
        SetPhase(Phase.Start);
        Say($"SORTIE {wave}", "BRING THE HOSTAGES HOME", StartSeconds + 0.3);
        Play(Sfx.LevelStart);
        Music(true);
    }

    private void Simulate(double dt, Controls c)
    {
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                _playTime += dt;
                if (!Calm && !Alert && _playTime > AlertSeconds) Alert = true;
                Fly(dt, c);
                if (Phase != Phase.Play) break;
                MoveHostages(dt);
                MoveEnemy(dt);
                if (Phase != Phase.Play) break;
                MoveShots(dt);
                if (Phase != Phase.Play) break;
                Collide();
                if (Phase == Phase.Play && Unaccounted == 0) Finish();
                break;
            case Phase.Dying:
                Fall(dt);
                MoveHostages(dt);
                MoveShots(dt);
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    if (Unaccounted == 0) { Finish(); break; }
                    Restart();
                    SetPhase(Phase.Play);
                }
                break;
            case Phase.Clear:
                MoveHostages(dt);
                if (PhaseTime >= 1.2 && PhaseTime - dt < 1.2 && Bonus > 0)
                {
                    Score += Bonus;
                    CheckExtra();
                }
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        Follow(dt);
        PhaseTime += dt;
    }

    /// <summary>The screen keeps the chopper a little behind the middle, looking the way it faces.</summary>
    private void Follow(double dt)
    {
        double want = Math.Clamp(Player.X - Field.Width / 2 + Player.Side * 150, 0, Field.WorldWidth - Field.Width);
        Camera += (want - Camera) * Math.Min(1, 2.5 * dt);
    }

    // ───────────────────────────── the chopper ─────────────────────────────

    /// <summary>
    /// One step of flying: across with momentum, up and down directly, turned to face the way it's pushed (or the
    /// front, while that's held). It sets down when it reaches the ground slowly enough; too fast and it's left
    /// touching the ground still flying, which the game counts as a crash. Up lifts off.
    /// </summary>
    public static void Step(Chopper p, Controls c, double dt)
    {
        // Pushed across while facing the front, it'll turn that way when the front's let go.
        if (c.Dx != 0) p.Side = Math.Sign(c.Dx);
        int want = c.Front ? 0 : p.Side;
        if (want != p.Facing)
        {
            p.Facing = want;
            p.Turning = TurnSeconds;
        }
        if (p.Landed)
        {
            p.Vx = p.Vy = 0;
            if (c.Dy >= 0) return;
            p.Landed = false;
        }
        double target = c.Dx * MaxSpeed;
        p.Vx += Math.Clamp(target - p.Vx, -Accel * dt, Accel * dt);
        p.Vy = c.Dy * Climb;
        double x = p.X + p.Vx * dt;
        p.X = Math.Clamp(x, Chopper.HalfWidth, Field.WorldWidth - Chopper.HalfWidth);
        if (p.X != x) p.Vx = 0;
        p.Y = Math.Max(Field.Ceiling + Chopper.HalfHeight, p.Y + p.Vy * dt);
        if (p.Y >= Chopper.LandedY)
        {
            p.Y = Chopper.LandedY;
            if (Math.Abs(p.Vx) <= LandSpeed)
            {
                p.Landed = true;
                p.Vx = p.Vy = 0;
            }
        }
    }

    /// <summary>Whether the chopper is touching the ground without having landed: it came down too fast.</summary>
    public static bool Crashing(Chopper p) => !p.Landed && p.Y >= Chopper.LandedY - 0.01;

    private void Fly(double dt, Controls c)
    {
        var p = Player;
        p.Safe = Math.Max(0, p.Safe - dt);
        p.FireIn -= dt;
        p.Turning -= dt;
        p.BoardIn -= dt;
        p.Rotor += dt;
        bool was = p.Landed;
        Step(p, c, dt);
        // A barracks roof can't be landed on: it holds the chopper up.
        foreach (var h in Huts)
            if (Math.Abs(p.X - h.X) < Barracks.HalfWidth + Chopper.HalfWidth - 6 && p.Y + Chopper.HalfHeight > Field.Ground - Barracks.Height)
            {
                p.Y = Field.Ground - Barracks.Height - Chopper.HalfHeight;
                p.Landed = false;
            }
        if (Crashing(p)) { Lose("crashed"); return; }
        if (p.Landed && !was)
        {
            Play(Sfx.Land);
            if (Tanks.Any(t => !t.Dead && Math.Abs(t.X - p.X) < Tank.HalfWidth + Chopper.HalfWidth - 8)) { Lose("crashed"); return; }
            foreach (var h in People.Where(h => !h.Gone && h.State != HostageState.Walking && Math.Abs(h.X - p.X) < 22)) LoseHostage(h, "crushed");
        }
        if (c.Fire && p.FireIn <= 0 && p.Turning <= 0)
        {
            if (p.Facing != 0 && Shots.Count(s => s.Kind == ShotKind.Bullet) < 3)
            {
                Shots.Add(new Shot { Kind = ShotKind.Bullet, X = p.X + p.Facing * Chopper.HalfWidth, Y = p.Y + 4, Vx = p.Facing * BulletSpeed + p.Vx * 0.4, Life = BulletLife });
                p.FireIn = FireGap;
                Play(Sfx.Fire);
            }
            else if (p.Facing == 0 && !p.Landed && Shots.Count(s => s.Kind == ShotKind.Bomb) < 2)
            {
                Shots.Add(new Shot { Kind = ShotKind.Bomb, X = p.X, Y = p.Y + Chopper.HalfHeight, Vx = p.Vx, Vy = BombDrop, Life = 5 });
                p.FireIn = BombGap;
                Play(Sfx.Bomb);
            }
        }
        // On the pad: everyone off, one at a time, to walk in.
        if (p.Landed && AtBase(p.X) && p.Aboard > 0 && p.BoardIn <= 0)
        {
            p.Aboard--;
            p.BoardIn = UnloadGap;
            People.Add(new Hostage { X = p.X + 24, Home = Field.DoorX, State = HostageState.Walking });
            Play(Sfx.Unload);
        }
    }

    public static bool AtBase(double x) => Math.Abs(x - Field.BaseX) < 80;

    /// <summary>A shot-down chopper falls to the ground.</summary>
    private void Fall(double dt)
    {
        var p = Player;
        if (p.Landed || p.Y >= Chopper.LandedY) return;
        p.Vy += Gravity * dt;
        p.Vx *= 1 - dt;
        p.X += p.Vx * dt;
        p.Y = Math.Min(Chopper.LandedY, p.Y + p.Vy * dt);
        if ((int)(PhaseTime * 20) != (int)((PhaseTime + dt) * 20))
            Effects.Add(new Effect { Kind = EffectKind.Smoke, X = p.X, Y = p.Y, Life = 1.2 });
        if (p.Y >= Chopper.LandedY)
        {
            Effects.Add(new Effect { Kind = EffectKind.Blast, X = p.X, Y = p.Y, Life = 0.9, Size = 2 });
            Play(Sfx.Blast);
        }
    }

    // ───────────────────────────── the hostages ─────────────────────────────

    /// <summary>
    /// Out of an open barracks one at a time; then milling about near it (and running from any tank that comes at
    /// them) until the chopper sets down within reach, when they run for it and climb aboard while there's room. Taken off without them, they wait again.
    /// Set down on the pad, they get off and walk in at the door.
    /// </summary>
    private void MoveHostages(double dt)
    {
        var p = Player;
        foreach (var hut in Huts.Where(h => h.Open && h.Inside > 0))
            if ((hut.OutIn -= dt) <= 0)
            {
                hut.Inside--;
                hut.OutIn = 0.35;
                People.Add(new Hostage { X = hut.X + (_rng.NextDouble() - 0.5) * 30, Home = hut.X, T = _rng.NextDouble() * 10 });
            }
        bool calling = p.Alive && p.Landed && !AtBase(p.X) && Phase == Phase.Play;
        foreach (var h in People)
        {
            if (h.Gone) continue;
            h.T += dt;
            switch (h.State)
            {
                case HostageState.Walking:
                    h.X += WalkSpeed * dt;
                    h.Walk += dt;
                    if (h.X >= Field.DoorX)
                    {
                        h.Gone = true;
                        Home++;
                        Rescued++;
                        Award(RescuePoints, h.X, Field.Ground - 40);
                    }
                    continue;
                case HostageState.Waiting when calling && p.Aboard < Chopper.Capacity && Math.Abs(p.X - h.X) < CallRange:
                    h.State = HostageState.Running;
                    break;
                case HostageState.Running when !calling || p.Aboard >= Chopper.Capacity:
                    h.State = HostageState.Waiting;
                    break;
            }
            if (h.State == HostageState.Running)
            {
                double dx = p.X - h.X;
                if (Math.Abs(dx) < 12)
                {
                    h.Vx = 0;
                    if (p.BoardIn <= 0)
                    {
                        h.Gone = true;
                        p.Aboard++;
                        p.BoardIn = BoardGap;
                        Play(Sfx.Board);
                    }
                }
                else
                {
                    h.Vx = Math.Sign(dx) * RunSpeed;
                    h.X += h.Vx * dt;
                    h.Walk += dt;
                }
            }
            else if (!Calm && Tanks.Where(t => !t.Dead && Math.Abs(t.Vx) > 1 && Math.Abs(t.X - h.X) < 140 && Math.Sign(h.X - t.X) == Math.Sign(t.Vx))
                         .OrderBy(t => Math.Abs(t.X - h.X)).FirstOrDefault() is { } tank)
            {
                // A tank coming: run from it.
                h.Vx = Math.Sign(h.X - tank.X) * RunSpeed;
                h.X += h.Vx * dt;
                h.Walk += dt;
            }
            else
            {
                // Milling about outside the barracks.
                double target = h.Home + Math.Sin(h.T * 0.45 + h.Home) * 70, dx = target - h.X;
                h.Vx = Math.Abs(dx) > 4 ? Math.Sign(dx) * WaitSpeed : 0;
                h.X += h.Vx * dt;
                if (h.Vx != 0) h.Walk += dt;
            }
            if (!Calm && Tanks.Any(t => !t.Dead && Math.Abs(t.Vx) > 1 && Math.Abs(t.X - h.X) < 18)) LoseHostage(h, "run over");
        }
        People.RemoveAll(h => h.Gone);
    }

    private void LoseHostage(Hostage h, string how)
    {
        if (h.Gone) return;
        h.Gone = true;
        Lost++;
        Effects.Add(new Effect { Kind = EffectKind.Word, X = h.X, Y = Field.Ground - 40, Text = how.ToUpperInvariant(), Colour = 0xFFFF6A5A, Life = 1.2 });
        Play(Sfx.HostageLost);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    private void MoveEnemy(double dt)
    {
        var p = Player;
        if (!Calm && Alert) Reinforce(dt);
        foreach (var t in Tanks.Where(t => !t.Dead))
        {
            t.T += dt;
            if (Calm) { t.Vx = 0; continue; }
            // Under the chopper, but never past the border.
            double target = Math.Min(p.X, Field.Border - 40), dx = target - t.X;
            t.Vx = Math.Abs(dx) > 30 ? Math.Sign(dx) * TankSpeed : 0;
            // Not up against another tank.
            if (t.Vx != 0 && Tanks.Any(o => o != t && !o.Dead && Math.Sign(o.X - t.X) == Math.Sign(t.Vx) && Math.Abs(o.X - t.X) < 80)) t.Vx = 0;
            t.X += t.Vx * dt;
            double gx = t.X, gy = Field.Ground - Tank.Height - 6;
            t.Aim = Math.Atan2(p.Y - gy, p.X - gx);
            t.FireIn -= dt;
            if (t.FireIn <= 0 && p.Alive && p.Safe <= 0 && !AtBase(p.X) && Math.Abs(p.X - t.X) < 560 && p.Y < Field.Ground - 40)
            {
                double lead = Field.Distance(gx, gy, p.X, p.Y) / ShellSpeed * 0.6, ax = p.X + p.Vx * lead, a = Math.Atan2(p.Y - gy, ax - gx);
                Shots.Add(new Shot { Kind = ShotKind.Shell, X = gx + Math.Cos(a) * 20, Y = gy + Math.Sin(a) * 20, Vx = Math.Cos(a) * ShellSpeed, Vy = Math.Sin(a) * ShellSpeed, Life = ShellLife });
                t.FireIn = TankFireGap * (0.8 + _rng.NextDouble() * 0.4);
                Play(Sfx.TankFire);
            }
        }
        foreach (var j in Jets.Where(j => !j.Dead && !j.Gone))
        {
            j.X += j.Vx * dt;
            if (Calm) continue;
            int dir = Math.Sign(j.Vx);
            double ahead = (p.X - j.X) * dir;
            // Coming at the chopper: down (or up) to its height. Past it: back up to cruise.
            double wantY = ahead > 0 && ahead < 800 && p.Alive ? Math.Clamp(p.Y, Field.Ceiling + 20, Field.Ground - 90) : j.Cruise;
            j.Vy = Math.Clamp((wantY - j.Y) * 2.2, -220, 220);
            j.Y += j.Vy * dt;
            j.FireIn -= dt;
            if (j.FireIn <= 0 && p.Alive && p.Safe <= 0 && ahead > 140 && ahead < 520 && Math.Abs(p.Y - j.Y) < 40)
            {
                Shots.Add(new Shot { Kind = ShotKind.Missile, X = j.X + dir * Jet.HalfWidth, Y = j.Y + 4, Vx = dir * MissileSpeed, Life = MissileLife });
                j.FireIn = 2.5;
                Play(Sfx.JetFire);
            }
            // Turning back, out of sight; after a few passes it's away home.
            bool past = ahead < -650 || (dir > 0 && j.X > Field.Border);
            if (past)
            {
                if (++j.Passes >= 3) j.Gone = true;
                else { j.Vx = -j.Vx; j.FireIn = 1; }
            }
            if (j.X < -300 || j.X > Field.WorldWidth + 300) j.Gone = true;
        }
        Jets.RemoveAll(j => j.Gone || j.Dead);
        foreach (var m in Mines.Where(m => !m.Dead))
        {
            m.T += dt;
            if (Calm) continue;
            double dx = p.X - m.X, dy = p.Y - m.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            m.Vx += (dx / d * MineSpeed - m.Vx) * Math.Min(1, 1.2 * dt);
            m.Vy += (dy / d * MineSpeed - m.Vy) * Math.Min(1, 1.2 * dt);
            m.X += m.Vx * dt;
            m.Y = Math.Clamp(m.Y + m.Vy * dt, Field.Ceiling, Field.Ground - 40);
        }
        Mines.RemoveAll(m => m.Dead);
        Tanks.RemoveAll(t => t.Dead);
    }

    /// <summary>More of the enemy, from out of sight on its own side: tanks along the ground, jets, and (later) mines.</summary>
    private void Reinforce(double dt)
    {
        var p = Player;
        double left = Camera - 140, right = Camera + Field.Width + 140;
        if ((_tankIn -= dt) <= 0)
        {
            _tankIn = 4 + _rng.NextDouble() * 3;
            if (Tanks.Count < TankLimit)
            {
                double x = left > 40 ? left : right < Field.Border - 60 ? right : double.NaN;
                if (!double.IsNaN(x)) Tanks.Add(new Tank { X = x, FireIn = TankFireGap });
            }
        }
        if ((_jetIn -= dt) <= 0)
        {
            _jetIn = JetGap * (0.8 + _rng.NextDouble() * 0.4);
            bool fromLeft = left > 0 || right > Field.Border;
            double y = Field.Ceiling + 30 + _rng.NextDouble() * 160;
            Jets.Add(new Jet { X = fromLeft ? left - 200 : right + 200, Y = y, Cruise = y, Vx = fromLeft ? JetSpeed : -JetSpeed, FireIn = 1 });
            Play(Sfx.JetPass);
        }
        if (MineLimit > 0 && (_mineIn -= dt) <= 0)
        {
            _mineIn = 9 + _rng.NextDouble() * 4;
            if (Mines.Count < MineLimit && p.X < Field.Border)
                Mines.Add(new Mine { X = Camera + _rng.NextDouble() * Field.Width, Y = Field.Ceiling - 30 });
        }
    }

    // ───────────────────────────── shots ─────────────────────────────

    private void MoveShots(double dt)
    {
        var p = Player;
        foreach (var s in Shots)
        {
            s.Life -= dt;
            if (s.Kind == ShotKind.Bomb) s.Vy += Gravity * dt;
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            if (s.Life <= 0) continue;
            switch (s.Kind)
            {
                case ShotKind.Bullet:
                    if (s.Y >= Field.Ground) { s.Life = 0; break; }
                    if (Tanks.FirstOrDefault(t => !t.Dead && Math.Abs(s.X - t.X) < Tank.HalfWidth && s.Y > Field.Ground - Tank.Height) is { } tank)
                    {
                        s.Life = 0;
                        KillTank(tank);
                    }
                    else if (Huts.FirstOrDefault(h => !h.Open && Math.Abs(s.X - h.X) < Barracks.HalfWidth && s.Y > Field.Ground - Barracks.Height) is { } hut)
                    {
                        s.Life = 0;
                        HitHut(hut);
                    }
                    else if (Jets.FirstOrDefault(j => !j.Dead && Math.Abs(s.X - j.X) < Jet.HalfWidth && Math.Abs(s.Y - j.Y) < Jet.HalfHeight + 4) is { } jet)
                    {
                        s.Life = 0;
                        jet.Dead = true;
                        Blast(jet.X, jet.Y, 1.3);
                        Award(JetPoints, jet.X, jet.Y);
                    }
                    else if (Mines.FirstOrDefault(m => !m.Dead && Field.Distance(m.X, m.Y, s.X, s.Y) < Mine.Radius + 4) is { } mine)
                    {
                        s.Life = 0;
                        mine.Dead = true;
                        Blast(mine.X, mine.Y, 0.9);
                        Award(MinePoints, mine.X, mine.Y);
                    }
                    else if (People.FirstOrDefault(h => !h.Gone && s.Y > Field.Ground - 22 && Math.Abs(h.X - s.X) < 7) is { } h)
                    {
                        s.Life = 0;
                        LoseHostage(h, "shot");
                    }
                    break;
                case ShotKind.Bomb:
                    var hitTank = Tanks.FirstOrDefault(t => !t.Dead && Math.Abs(s.X - t.X) < Tank.HalfWidth && s.Y > Field.Ground - Tank.Height);
                    var hitHut = Huts.FirstOrDefault(h => !h.Open && Math.Abs(s.X - h.X) < Barracks.HalfWidth && s.Y > Field.Ground - Barracks.Height);
                    if (hitTank == null && hitHut == null && s.Y < Field.Ground - 4) break;
                    s.Life = 0;
                    Blast(s.X, Math.Min(s.Y, Field.Ground - 6), 1);
                    if (hitHut != null) HitHut(hitHut);
                    foreach (var t in Tanks.Where(t => !t.Dead && Math.Abs(s.X - t.X) < Tank.HalfWidth + 16)) KillTank(t);
                    foreach (var h in People.Where(h => !h.Gone && h.State != HostageState.Walking && Math.Abs(h.X - s.X) < BlastRadius)) LoseHostage(h, "blown up");
                    break;
                default:
                    if (p.Alive && p.Safe <= 0 && Math.Abs(s.X - p.X) < Chopper.HalfWidth - 4 && Math.Abs(s.Y - p.Y) < Chopper.HalfHeight + 2)
                    {
                        s.Life = 0;
                        Lose("shot down");
                        return;
                    }
                    if (s.Y > Field.Ground || s.Y < Field.Hud) s.Life = 0;
                    break;
            }
        }
        Shots.RemoveAll(s => s.Life <= 0);
    }

    private void KillTank(Tank t)
    {
        if (t.Dead) return;
        t.Dead = true;
        Blast(t.X, Field.Ground - 14, 1.4);
        Award(TankPoints, t.X, Field.Ground - 40);
    }

    private void HitHut(Barracks h)
    {
        h.Hit = 0.15;
        if (++h.Hits < Barracks.Toughness) { Play(Sfx.BarracksHit); return; }
        h.Open = true;
        h.OutIn = 0.3;
        Alert = true;
        Blast(h.X, Field.Ground - Barracks.Height, 1.6);
        Play(Sfx.BarracksOpen);
    }

    private void Blast(double x, double y, double size)
    {
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = x, Y = y, Life = 0.7, Size = size });
        Play(Sfx.Blast);
    }

    /// <summary>The chopper flying into a jet, a mine, or a tank on the ground.</summary>
    private void Collide()
    {
        var p = Player;
        foreach (var h in Huts) h.Hit = Math.Max(0, h.Hit - Field.Step);
        if (!p.Alive || p.Safe > 0) return;
        if (Jets.FirstOrDefault(j => !j.Dead && Math.Abs(j.X - p.X) < Jet.HalfWidth + Chopper.HalfWidth - 10 && Math.Abs(j.Y - p.Y) < Jet.HalfHeight + Chopper.HalfHeight - 4) is { } jet)
        {
            jet.Dead = true;
            Blast(jet.X, jet.Y, 1.3);
            Lose("hit by a jet");
            return;
        }
        if (Mines.FirstOrDefault(m => !m.Dead && Math.Abs(m.X - p.X) < Chopper.HalfWidth + Mine.Radius - 8 && Math.Abs(m.Y - p.Y) < Chopper.HalfHeight + Mine.Radius - 4) is { } mine)
        {
            mine.Dead = true;
            Blast(mine.X, mine.Y, 1);
            Lose("hit a mine");
            return;
        }
        if (!p.Landed && Tanks.Any(t => !t.Dead && Math.Abs(t.X - p.X) < Tank.HalfWidth + Chopper.HalfWidth - 10 && p.Y + Chopper.HalfHeight > Field.Ground - Tank.Height))
            Lose("crashed");
    }

    private void Lose(string cause)
    {
        var p = Player;
        Cause = cause;
        p.Alive = false;
        Lost += p.Aboard;
        p.Aboard = 0;
        Lives--;
        Play(Sfx.Crash);
        Blast(p.X, p.Y, 1.6);
        SetPhase(Phase.Dying);
        foreach (var h in People.Where(h => h.State == HostageState.Running)) h.State = HostageState.Waiting;
        Say(Lives <= 0 ? "GAME OVER" : "CHOPPER DOWN", null, DyingSeconds);
    }

    /// <summary>Everyone home or lost: a bonus for those home, and more if none were lost.</summary>
    private void Finish()
    {
        Bonus = HomeBonus * Home * Wave + (Lost == 0 ? SafeBonus : 0);
        Shots.Clear();
        Play(Sfx.LevelClear);
        Say(Lost == 0 ? "EVERYONE HOME!" : "SORTIE OVER", $"{Home} HOME   {Lost} LOST   BONUS {Bonus}", ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Award(int points, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y - 20, Text = points.ToString(), Life = 0.8, Colour = 0xFFFFE14A });
        CheckExtra();
    }

    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a sortie, past its number.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Player.Safe = 0;
        Message = SubMessage = null;
    }

    internal void PutChopper(double x, double y, int facing = -1)
    {
        var p = Player;
        p.X = x;
        p.Y = Math.Min(y, Chopper.LandedY);
        p.Landed = y >= Chopper.LandedY;
        p.Vx = p.Vy = 0;
        p.Facing = facing;
        if (facing != 0) p.Side = facing;
        p.Turning = 0;
        p.Safe = 0;
        p.Alive = true;
        Camera = Math.Clamp(x - Field.Width / 2, 0, Field.WorldWidth - Field.Width);
    }

    internal Tank PutTank(double x)
    {
        var t = new Tank { X = x, FireIn = 0 };
        Tanks.Add(t);
        return t;
    }

    internal Jet PutJet(double x, double y, double vx)
    {
        var j = new Jet { X = x, Y = y, Cruise = y, Vx = vx, FireIn = 0 };
        Jets.Add(j);
        return j;
    }

    internal Mine PutMine(double x, double y)
    {
        var m = new Mine { X = x, Y = y };
        Mines.Add(m);
        return m;
    }

    internal void OpenHut(int i)
    {
        Huts[i].Hits = Barracks.Toughness - 1;
        HitHut(Huts[i]);
    }

    internal void SetAlert(bool on) => Alert = on;
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
