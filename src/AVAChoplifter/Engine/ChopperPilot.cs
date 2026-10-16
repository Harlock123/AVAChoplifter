namespace AVAChoplifter.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). It works the strip one job at a time: shoot open
/// the nearest closed barracks from low down beside it, set down near whoever is waiting outside and stay while
/// they run aboard, and fly them home to the pad when the chopper is full or there's nobody left to fetch.
/// Along the way it turns on a jet or mine at its own height, bombs a tank that comes close, and if anything is
/// about to hit it, takes whichever of the nine ways to fly keeps it clearest.
/// </summary>
public sealed class ChopperPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    /// <summary>Where it flies between jobs: high enough to see the tanks' shells coming, low enough to get down fast.</summary>
    private const double Cruise = Field.Ground - 250;
    /// <summary>Where it hovers to shoot a barracks: its bullets at the walls, its skids clear of the roofline's edge.</summary>
    private const double Shooting = Field.Ground - 34;
    private const double ShootFrom = 230;

    public Controls Decide(GameEngine g)
    {
        var p = g.Player;
        if (g.Phase is not (Phase.Play or Phase.Start) || !p.Alive) { Doing = "waiting"; return default; }
        if (g.Phase == Phase.Start) { Doing = "on the pad"; return default; }

        if (p.Landed)
        {
            if (GameEngine.AtBase(p.X) && p.Aboard > 0) { Doing = "unloading"; return default; }
            bool coming = p.Aboard < Chopper.Capacity && g.People.Any(h => h.State == HostageState.Running && Math.Abs(h.X - p.X) < GameEngine.CallRange);
            bool danger = Clearance(g, default) < 8 || g.Tanks.Any(t => Math.Abs(t.X - p.X) < 200);
            if (coming && !danger) { Doing = "boarding"; return default; }
            if (!GameEngine.AtBase(p.X) || Job(g).Kind != "home") { Doing = "taking off"; return Safe(g, new Controls(0, -1), 6); }
            Doing = "on the pad";
            return default;
        }

        // A jet or mine at its own height and not far off: turn on it and fire.
        if (g.Alert && Target(g) is { } aim)
        {
            Doing = "shooting it down";
            return Safe(g, new Controls(Math.Sign(aim), Math.Sign(Cruise - p.Y) * (Math.Abs(Cruise - p.Y) > 120 ? 1 : 0), Fire: p.Facing == Math.Sign(aim)), 10);
        }
        // A mine drifting in: get level with it and shoot it.
        if (g.Mines.Where(m => !m.Dead && Field.Distance(m.X, m.Y, p.X, p.Y) < 380).OrderBy(m => Field.Distance(m.X, m.Y, p.X, p.Y)).FirstOrDefault() is { } mine)
        {
            double dy = mine.Y - (p.Y + 4);
            int side = Math.Sign(mine.X - p.X);
            Doing = "after a mine";
            var c = new Controls(Math.Abs(mine.X - p.X) < 120 ? -side : p.Facing == side ? 0 : side, Math.Abs(dy) > 6 ? Math.Sign(dy) : 0, Fire: p.Facing == side && Math.Abs(dy) < Mine.Radius);
            if (p.Y > Field.Ground - 70 && c.Dy > 0) c = c with { Dy = 0 };
            return Safe(g, c, 8);
        }
        // A tank close to where it's working: bomb it.
        var job = Job(g);
        if (g.Alert && p.Aboard < Chopper.Capacity - 2 && g.Tanks.Where(t => Math.Abs(t.X - p.X) < 420 || Math.Abs(t.X - job.X) < 320)
                .OrderBy(t => Math.Abs(t.X - p.X)).FirstOrDefault() is { } tank)
            return Bomb(g, tank);

        switch (job.Kind)
        {
            case "barracks":
            {
                double dx = job.X - p.X, dy = Shooting - p.Y;
                if (Math.Abs(dx) < 60 && Math.Abs(dy) < 10)
                {
                    Doing = "shooting the barracks";
                    // Face the barracks (a nudge that way), then fire.
                    int face = Math.Sign(job.Face);
                    var c = p.Facing == face ? new Controls(Fire: true) : new Controls(face, 0);
                    return Safe(g, c, 6);
                }
                Doing = "to the barracks";
                return Safe(g, Toward(p, job.X, Math.Abs(dx) < 220 ? Shooting : Cruise), 10);
            }
            case "pick up":
            case "home":
            {
                double dx = job.X - p.X;
                Doing = job.Kind == "home" ? "taking them home" : "to the hostages";
                if (Math.Abs(dx) < 24 && Math.Abs(p.Vx) < 90)
                {
                    // Over the spot: straight down, unless something's underneath now.
                    bool clear = !g.Tanks.Any(t => Math.Abs(t.X - p.X) < 90) && !g.People.Any(h => h.State != HostageState.Walking && Math.Abs(h.X - p.X) < 40);
                    Doing = "landing";
                    // Someone wandered underneath on the way down: back up a little and wait.
                    return Safe(g, new Controls(0, clear ? 1 : p.Y > Field.Ground - 70 ? -1 : 0), 6);
                }
                return Safe(g, Toward(p, job.X, Math.Abs(dx) < 260 ? Field.Ground - 120 : Cruise), 10);
            }
            default:
                Doing = "patrolling";
                return Safe(g, Toward(p, Field.BaseX - 600, Cruise), 10);
        }
    }

    /// <summary>The job in hand: where to go, and what to do there.</summary>
    private static (string Kind, double X, double Face) Job(GameEngine g)
    {
        var p = g.Player;
        var waiting = g.People.Where(h => h.State != HostageState.Walking).ToList();
        int left = g.Huts.Sum(h => h.Inside) + waiting.Count;
        if (p.Aboard >= Chopper.Capacity || (p.Aboard > 0 && left == 0) || (p.Aboard >= 10 && waiting.Count == 0))
            return ("home", Field.BaseX, 0);
        if (waiting.Count > 0)
        {
            // The group nearest the chopper; land just on the base side of it, clear of everyone.
            var near = waiting.OrderBy(h => Math.Abs(h.X - p.X)).First();
            var group = waiting.Where(h => Math.Abs(h.X - near.Home) < 160 || Math.Abs(h.X - near.X) < 160).ToList();
            double spot = group.Max(h => h.X) + 70;
            while (g.People.Any(h => h.State != HostageState.Walking && Math.Abs(h.X - spot) < 30) || g.Huts.Any(h => Math.Abs(h.X - spot) < Barracks.HalfWidth + Chopper.HalfWidth))
                spot += 30;
            return ("pick up", spot, 0);
        }
        var hut = g.Huts.Where(h => !h.Open && h.Inside > 0).OrderBy(h => Math.Abs(h.X + ShootFrom - p.X)).FirstOrDefault();
        if (hut != null) return ("barracks", hut.X + ShootFrom, -1);
        return p.Aboard > 0 ? ("home", Field.BaseX, 0) : ("idle", Field.BaseX - 600, 0);
    }

    /// <summary>A jet or mine its bullets would meet: which way (-1 or 1), or null.</summary>
    private static double? Target(GameEngine g)
    {
        var p = g.Player;
        double y = p.Y + 4;
        foreach (var j in g.Jets.Where(j => !j.Dead))
        {
            double dx = j.X - p.X;
            if (Math.Abs(j.Y - y) < Jet.HalfHeight + 2 && Math.Abs(dx) is > 60 and < 560) return Math.Sign(dx);
        }
        foreach (var m in g.Mines.Where(m => !m.Dead))
        {
            double dx = m.X - p.X;
            if (Math.Abs(m.Y - y) < Mine.Radius && Math.Abs(dx) is > 50 and < 500) return Math.Sign(dx);
        }
        return null;
    }

    /// <summary>Over a tank facing the front, keeping where the bomb will land on where the tank will be.</summary>
    private Controls Bomb(GameEngine g, Tank t)
    {
        var p = g.Player;
        double y0 = p.Y + Chopper.HalfHeight, fall = Field.Ground - 4 - y0;
        double tf = (-GameEngine.BombDrop + Math.Sqrt(GameEngine.BombDrop * GameEngine.BombDrop + 2 * GameEngine.Gravity * Math.Max(0, fall))) / GameEngine.Gravity;
        double lands = p.X + p.Vx * tf, at = t.X + t.Vx * tf, off = at - lands;
        // Steer the speed so the landing spot closes on the tank.
        double wantVx = Math.Clamp(off * 1.6, -GameEngine.MaxSpeed, GameEngine.MaxSpeed);
        int dx = Math.Abs(wantVx - p.Vx) < 30 ? 0 : Math.Sign(wantVx - p.Vx);
        double wantY = Field.Ground - 200;
        int dy = Math.Abs(wantY - p.Y) > 14 ? Math.Sign(wantY - p.Y) : 0;
        bool hostages = g.People.Any(h => h.State != HostageState.Walking && Math.Abs(h.X - at) < GameEngine.BlastRadius + 16);
        Doing = "bombing a tank";
        return Safe(g, new Controls(dx, dy, Math.Abs(off) < 20 && !hostages && p.Facing == 0, Front: true), 10);
    }

    /// <summary>To a spot at a height, slowing in time to stop over it.</summary>
    private static Controls Toward(Chopper p, double x, double y)
    {
        double dx = x - p.X, stop = p.Vx * p.Vx / (2 * GameEngine.Accel) + 12;
        int ax;
        if (Math.Abs(dx) > stop && Math.Abs(dx) > 10) ax = Math.Sign(dx);
        else ax = Math.Abs(p.Vx) > 30 ? -Math.Sign(p.Vx) : Math.Abs(dx) > 8 ? Math.Sign(dx) : 0;
        int ay = Math.Abs(y - p.Y) > 8 ? Math.Sign(y - p.Y) : 0;
        // Never let it reach the ground still going fast.
        if (ay > 0 && p.Y > Field.Ground - 60 && Math.Abs(p.Vx) > GameEngine.LandSpeed * 0.7) ay = 0;
        return new Controls(ax, ay);
    }

    private Controls Safe(GameEngine g, Controls want, double enough)
    {
        double here = Clearance(g, want);
        if (here > enough) return want;
        var best = want;
        double most = here;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            var c = want with { Dx = dx, Dy = dy, Fire = want.Fire };
            double clear = Clearance(g, c);
            if (clear > most + 2) { most = clear; best = c; }
        }
        if (best != want) Doing = "dodging";
        return best;
    }

    /// <summary>How close the nearest danger comes over the next half second or so, flying like this: shells, missiles, jets, mines, or the ground too fast.</summary>
    private static double Clearance(GameEngine g, Controls c)
    {
        var p = g.Player;
        var me = new Chopper { X = p.X, Y = p.Y, Vx = p.Vx, Vy = p.Vy, Landed = p.Landed, Facing = p.Facing, Side = p.Side };
        var shots = g.Shots.Where(s => s.Hostile).ToList();
        double least = double.MaxValue;
        for (int t = 1; t <= 36; t++)
        {
            double dt = t * Field.Step;
            GameEngine.Step(me, c, Field.Step);
            if (GameEngine.Crashing(me)) return -100;
            if (me.Landed && !p.Landed && g.People.Any(h => h.State != HostageState.Walking && Math.Abs(h.X - me.X) < 30)) return -50;
            foreach (var s in shots)
                least = Math.Min(least, Gap(s.X + s.Vx * dt, s.Y + s.Vy * dt, me, 0, 0));
            foreach (var j in g.Jets.Where(j => !j.Dead))
                least = Math.Min(least, Gap(j.X + j.Vx * dt, j.Y + j.Vy * dt, me, Jet.HalfWidth - 6, Jet.HalfHeight));
            foreach (var m in g.Mines.Where(m => !m.Dead))
                least = Math.Min(least, Gap(m.X + m.Vx * dt, m.Y + m.Vy * dt, me, Mine.Radius, Mine.Radius));
            if (!me.Landed)
                foreach (var tank in g.Tanks)
                    least = Math.Min(least, Gap(tank.X + tank.Vx * dt, Field.Ground - Tank.Height / 2, me, Tank.HalfWidth - 6, Tank.Height / 2));
        }
        return least;
    }

    /// <summary>How far a thing at (x, y), of the given half-size, is from touching the chopper.</summary>
    private static double Gap(double x, double y, Chopper me, double hw, double hh)
    {
        double gx = Math.Abs(x - me.X) - Chopper.HalfWidth - hw, gy = Math.Abs(y - me.Y) - Chopper.HalfHeight - hh;
        return Math.Max(gx, gy);
    }
}
