using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAChoplifter.Engine;
using Effect = AVAChoplifter.Engine.Effect;

namespace AVAChoplifter.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAChoplifter/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Sand = 0xFFD8B070, Olive = 0xFF5A6A3A, Orange = 0xFFFF8A2A;

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

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    /// <summary>Strip to screen, across.</summary>
    private double Sx(double wx) => wx - Engine.Camera;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF101018), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            DrawLand(dc, g.Camera);
            foreach (var h in g.Huts) DrawBarracks(dc, Sx(h.X), h.Open, h.Hit > 0, h.Inside);
            foreach (var t in g.Tanks) DrawTank(dc, Sx(t.X), t.Aim, t.T, t.Vx, 1);
            foreach (var h in g.People) DrawHostage(dc, Sx(h.X), Field.Ground, h.Walk, h.State != HostageState.Waiting || h.Vx != 0, Waving(h), h.Vx, 1.3);
            foreach (var s in g.Shots) DrawShot(dc, s);
            foreach (var j in g.Jets) DrawJet(dc, Sx(j.X), j.Y, Math.Sign(j.Vx), j.Vy, 1);
            foreach (var m in g.Mines) DrawMine(dc, Sx(m.X), m.Y, m.T, 1);
            DrawPlayer(dc);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 22), 26, Yellow, center: true);
    }

    /// <summary>Waiting hostages wave when the chopper is about.</summary>
    private bool Waving(Hostage h) => h.State == HostageState.Waiting && Engine.Player.Alive && Math.Abs(Engine.Player.X - h.X) < 600;

    // ───────────────────────────── the land ─────────────────────────────

    /// <summary>A dusk sky, far mountains and nearer dunes going by slower than the ground, the sand, the border fence and the base.</summary>
    private void DrawLand(DrawingContext dc, double camera)
    {
        double top = Field.Hud, horizon = Field.Ground - 120;
        uint[] bands = [0xFF1E2450, 0xFF2E2E62, 0xFF4A3A70, 0xFF6E4876, 0xFF9A5A72, 0xFFC8706A, 0xFFE8905E, 0xFFF2B068];
        double bh = (horizon - top) / bands.Length;
        for (int i = 0; i < bands.Length; i++) dc.FillRectangle(Brush(bands[i]), new Rect(0, top + i * bh, Field.Width, bh + 1));
        dc.FillRectangle(Brush(0xFFF2B068), new Rect(0, horizon, Field.Width, Field.Ground - horizon));
        // A low sun, and a few stars still up.
        dc.DrawEllipse(Brush(0x50FFE0A0), null, new Point(820 - camera * 0.02 % 200, horizon - 40), 60, 60);
        dc.DrawEllipse(Brush(0xFFFFE6A8), null, new Point(820 - camera * 0.02 % 200, horizon - 40), 34, 34);
        for (int i = 0; i < 50; i++)
        {
            uint q = Hash(i, 3, 3);
            double sx = ((q % 3000) - camera * 0.05) % 1024;
            if (sx < 0) sx += 1024;
            dc.FillRectangle(Brush(0xA0FFFFFF), new Rect(sx, top + (q >> 12) % 150, 2, 2));
        }
        Ridge(dc, camera * 0.25, horizon + 10, 120, 0xFF5A4268, 7, 160);
        Ridge(dc, camera * 0.5, Field.Ground - 30, 60, 0xFFB0784E, 11, 110);
        // The sand, with stones and scrub going by.
        dc.FillRectangle(Brush(Sand), new Rect(0, Field.Ground, Field.Width, Field.Height - Field.Ground));
        dc.FillRectangle(Brush(0xFFB8905A), new Rect(0, Field.Ground, Field.Width, 3));
        int first = (int)Math.Floor(camera / 40) - 1;
        for (int i = first; i < first + 30; i++)
        {
            uint q = Hash(i, 7, 1);
            double x = i * 40 - camera + q % 40;
            dc.FillRectangle(Brush(0xFFB89058), new Rect(x, Field.Ground + 10 + (q >> 8) % 50, 4 + q % 5, 2));
            if (q % 7 == 0)
            {
                for (int k = -2; k <= 2; k++) dc.DrawLine(Pen(0xFF6A7A3A, 2), new Point(x, Field.Ground), new Point(x + k * 4, Field.Ground - 10 + Math.Abs(k) * 2));
            }
        }
        // The border: a fence of posts and wire.
        double bx = Sx(Field.Border);
        if (bx > -40 && bx < Field.Width + 40)
        {
            for (int i = -2; i <= 2; i++) dc.FillRectangle(Brush(0xFF5A4A3A), new Rect(bx + i * 14 - 2, Field.Ground - 40, 4, 40));
            for (int k = 0; k < 3; k++) dc.DrawLine(Pen(0xFF9A9A9A, 1), new Point(bx - 30, Field.Ground - 36 + k * 12), new Point(bx + 30, Field.Ground - 36 + k * 12));
            Text(dc, "BORDER", new Point(bx, Field.Ground - 54), 18, 0xFFFFE6C0, center: true);
        }
        DrawBase(dc, Sx(Field.BaseX), Sx(Field.DoorX));
    }

    /// <summary>A line of peaks across the screen, going by at its own rate.</summary>
    private static void Ridge(DrawingContext dc, double offset, double baseY, double height, uint colour, int salt, double step)
    {
        int first = (int)Math.Floor(offset / step) - 1;
        var pts = new List<Point> { new(-10, baseY + 40) };
        for (int i = first; i < first + (int)(1100 / step) + 3; i++)
        {
            uint q = Hash(i, salt, 5);
            pts.Add(new Point(i * step - offset, baseY - height * (0.35 + (q % 100) / 160.0)));
            pts.Add(new Point(i * step - offset + step / 2, baseY - height * (0.1 + ((q >> 8) % 100) / 400.0)));
        }
        pts.Add(new Point(1040, baseY + 40));
        Poly(dc, colour, null, 0, [.. pts]);
        dc.FillRectangle(Brush(colour), new Rect(0, baseY + 38, Field.Width, Field.Ground - baseY - 38));
    }

    /// <summary>Home: a pad with a big H, a hangar with its door, a mast with a windsock.</summary>
    private void DrawBase(DrawingContext dc, double pad, double door)
    {
        if (pad < -200 || pad > Field.Width + 300) return;
        double g = Field.Ground;
        dc.FillRectangle(Brush(0xFF7A7A72), new Rect(pad - 70, g - 4, 140, 8));
        dc.DrawEllipse(null, Pen(0xFFF2F2E6, 3), new Point(pad, g), 50, 5);
        Text(dc, "H", new Point(pad, g + 18), 28, 0xFFF2F2E6, center: true, shadow: false);
        // The hangar.
        Poly(dc, 0xFF8A8478, 0xFF4A4640, 2, new Point(door - 40, g), new Point(door - 40, g - 70), new Point(door + 40, g - 110), new Point(door + 220, g - 110), new Point(door + 220, g));
        for (int i = 0; i < 6; i++) dc.DrawLine(Pen(0xFF6A665E, 1.5), new Point(door + 10 + i * 34, g - 100), new Point(door + 10 + i * 34, g));
        dc.FillRectangle(Brush(0xFF2A2620), new Rect(door - 8, g - 52, 40, 52));
        dc.FillRectangle(Brush(0xFFFFD27A), new Rect(door - 4, g - 48, 32, 48));
        Text(dc, "HOME BASE", new Point(door + 110, g - 80), 24, 0xFF2A2620, center: true, shadow: false);
        // The mast and windsock.
        double mx = pad - 120;
        dc.DrawLine(Pen(0xFF4A4A4A, 3), new Point(mx, g), new Point(mx, g - 110));
        double flap = Math.Sin(_clock * 6) * 3;
        Poly(dc, Orange, 0xFF9A4A1A, 1, new Point(mx, g - 108), new Point(mx + 34, g - 102 + flap), new Point(mx + 34, g - 94 + flap), new Point(mx, g - 90));
        dc.FillRectangle(Brush(White), new Rect(mx + 12, g - 104 + flap / 2, 6, 12));
    }

    /// <summary>A long hut with a tin roof, barred windows and a door; shot open, its roof gone and its walls broken.</summary>
    private static void DrawBarracks(DrawingContext dc, double x, bool open, bool flash, int inside)
    {
        if (x < -120 || x > Field.Width + 120) return;
        double g = Field.Ground, hw = Barracks.HalfWidth, h = Barracks.Height;
        uint wall = flash ? 0xFFFFF0C0 : 0xFF9A7A5A;
        if (!open)
        {
            dc.FillRectangle(Brush(wall), new Rect(x - hw, g - h + 10, hw * 2, h - 10));
            Poly(dc, 0xFF7A8088, 0xFF4A4E54, 1.5, new Point(x - hw - 8, g - h + 12), new Point(x - hw + 10, g - h - 6), new Point(x + hw - 10, g - h - 6), new Point(x + hw + 8, g - h + 12));
            for (int i = 0; i < 7; i++) dc.DrawLine(Pen(0xFF5A6068, 1), new Point(x - hw + 4 + i * 20, g - h - 4), new Point(x - hw - 4 + i * 20, g - h + 11));
            foreach (double wx in new[] { -40.0, 40.0 })
            {
                dc.FillRectangle(Brush(0xFF2A2018), new Rect(x + wx - 10, g - h + 18, 20, 14));
                for (int b = 0; b < 4; b++) dc.FillRectangle(Brush(0xFF8A8A8A), new Rect(x + wx - 8 + b * 5, g - h + 18, 2, 14));
            }
            dc.FillRectangle(Brush(0xFF5A3A22), new Rect(x - 10, g - 28, 20, 28));
            dc.FillRectangle(Brush(0xFFC0A040), new Rect(x + 5, g - 15, 3, 3));
            return;
        }
        // Broken walls, smoke still going up if anyone's left inside.
        Poly(dc, 0xFF7A5E44, 0xFF4A3A2A, 1.5, new Point(x - hw, g), new Point(x - hw, g - 30), new Point(x - hw + 18, g - 22), new Point(x - hw + 30, g - 36),
            new Point(x - 10, g - 20), new Point(x + 14, g - 34), new Point(x + 40, g - 18), new Point(x + hw - 8, g - 32), new Point(x + hw, g - 24), new Point(x + hw, g));
        dc.FillRectangle(Brush(0xFF3A2A1E), new Rect(x - 12, g - 20, 24, 20));
        if (inside > 0) dc.DrawEllipse(Brush(0x60505050), null, new Point(x + 10, g - 50), 18, 12);
    }

    // ───────────────────────────── people and machines ─────────────────────────────

    /// <summary>A hostage: shirt and trousers, arms up to wave, or running.</summary>
    private static void DrawHostage(DrawingContext dc, double x, double ground, double walk, bool moving, bool waving, double vx, double k)
    {
        if (x < -20 || x > Field.Width + 20) return;
        double swing = moving ? Math.Sin(walk * 16) * 4 * k : 0;
        double y = ground;
        dc.DrawLine(Pen(0xFF3A3A5A, 2.5 * k), new Point(x - 1.5 * k, y - 9 * k), new Point(x - 1.5 * k + swing, y));
        dc.DrawLine(Pen(0xFF3A3A5A, 2.5 * k), new Point(x + 1.5 * k, y - 9 * k), new Point(x + 1.5 * k - swing, y));
        dc.FillRectangle(Brush(0xFFF2F2EA), new Rect(x - 3.5 * k, y - 18 * k, 7 * k, 10 * k), (float)(2 * k));
        if (waving)
        {
            double wv = Math.Sin(walk * 10 + x) * 3 * k;
            dc.DrawLine(Pen(0xFFF0C8A0, 2 * k), new Point(x - 3 * k, y - 16 * k), new Point(x - 7 * k + wv, y - 25 * k));
            dc.DrawLine(Pen(0xFFF0C8A0, 2 * k), new Point(x + 3 * k, y - 16 * k), new Point(x + 7 * k - wv, y - 25 * k));
        }
        else
        {
            dc.DrawLine(Pen(0xFFF0C8A0, 2 * k), new Point(x - 3 * k, y - 16 * k), new Point(x - 5 * k - swing * 0.6, y - 9 * k));
            dc.DrawLine(Pen(0xFFF0C8A0, 2 * k), new Point(x + 3 * k, y - 16 * k), new Point(x + 5 * k + swing * 0.6, y - 9 * k));
        }
        dc.DrawEllipse(Brush(0xFFF0C8A0), null, new Point(x + Math.Sign(vx) * 0.5 * k, y - 21 * k), 3.2 * k, 3.2 * k);
        dc.DrawEllipse(Brush(0xFF4A3020), null, new Point(x, y - 23 * k), 3 * k, 1.6 * k);
    }

    private void DrawPlayer(DrawingContext dc)
    {
        var g = Engine;
        var p = g.Player;
        if (p.Safe > 0 && p.Alive && g.Phase == Phase.Play && (int)(_clock * 12) % 2 == 0) return;
        double tilt = p.Landed ? 0 : p.Vx / GameEngine.MaxSpeed * 0.22;
        if (!p.Alive) tilt = Math.Min(0.9, g.PhaseTime * 1.5) * (p.Vx >= 0 ? 1 : -1);
        DrawChopper(dc, Sx(p.X), p.Y, p.Facing, tilt, p.Alive ? _clock : 0, p.Alive, 1);
        // Who's aboard, along the side.
        if (p.Alive && p.Aboard > 0)
            Text(dc, p.Aboard.ToString(), new Point(Sx(p.X), p.Y - 34), 18, p.Aboard >= Chopper.Capacity ? Red : White, center: true);
    }

    /// <summary>The rescue helicopter, side on (facing -1 or 1) or nose on (0), tilted into its flight, rotors turning.</summary>
    private static void DrawChopper(DrawingContext dc, double x, double y, int facing, double tilt, double rotor, bool alive, double k)
    {
        using var turn = dc.PushTransform(Matrix.CreateTranslation(-x, -y) * Matrix.CreateRotation(tilt) * Matrix.CreateTranslation(x, y));
        uint body = alive ? 0xFF3A6A4A : 0xFF3A3A34, dark = alive ? 0xFF22402C : 0xFF222220;
        double blur = Math.Abs(Math.Sin(rotor * 31));
        if (facing == 0)
        {
            // Nose on: a round cab, skids both sides, the rotor a long blur.
            dc.DrawLine(Pen(0xFF2A2A2A, 2.5 * k), new Point(x - 22 * k, y + 16 * k), new Point(x + 22 * k, y + 16 * k));
            dc.DrawLine(Pen(0xFF2A2A2A, 2 * k), new Point(x - 12 * k, y + 8 * k), new Point(x - 16 * k, y + 16 * k));
            dc.DrawLine(Pen(0xFF2A2A2A, 2 * k), new Point(x + 12 * k, y + 8 * k), new Point(x + 16 * k, y + 16 * k));
            dc.DrawEllipse(Brush(body), Pen(dark, 1.5), new Point(x, y), 18 * k, 14 * k);
            dc.DrawEllipse(Brush(0xFF9AD0E8), Pen(0xFF3A5A6A, 1), new Point(x, y - 3 * k), 12 * k, 8 * k);
            dc.DrawEllipse(Brush(0x80FFFFFF), null, new Point(x - 4 * k, y - 6 * k), 4 * k, 2.5 * k);
            dc.FillRectangle(Brush(0xFF2A2A2A), new Rect(x - 2 * k, y - 20 * k, 4 * k, 7 * k));
            dc.DrawLine(Pen(Alpha(0xFF1A1A1A, 0.5 + 0.4 * blur), 2.5 * k), new Point(x - 46 * k, y - 20 * k), new Point(x + 46 * k, y - 20 * k));
            return;
        }
        double f = facing;
        Point P(double px, double py) => new(x + px * f * k, y + py * k);
        // Skids.
        dc.DrawLine(Pen(0xFF2A2A2A, 2.5 * k), P(-24, 16), P(20, 16));
        dc.DrawLine(Pen(0xFF2A2A2A, 2.5 * k), P(20, 16), P(25, 12));
        dc.DrawLine(Pen(0xFF2A2A2A, 2 * k), P(-14, 8), P(-16, 16));
        dc.DrawLine(Pen(0xFF2A2A2A, 2 * k), P(10, 8), P(12, 16));
        // Tail boom, fin and tail rotor.
        Poly(dc, body, dark, 1.2, P(-12, -6), P(-54, -4), P(-54, 1), P(-12, 6));
        Poly(dc, body, dark, 1.2, P(-48, -4), P(-56, -16), P(-60, -16), P(-56, 1));
        double tr = Math.Sin(rotor * 40) * 8;
        dc.DrawLine(Pen(0xB0202020, 2 * k), P(-56, -6 - tr), P(-56, -6 + tr));
        // The cab: a round body with a big glass nose.
        dc.DrawEllipse(Brush(body), Pen(dark, 1.5), new Point(x, y), 26 * k, 14 * k);
        Poly(dc, 0xFF9AD0E8, 0xFF3A5A6A, 1, P(6, -12), P(18, -10), P(26, -2), P(25, 4), P(8, 4));
        dc.DrawLine(Pen(0x90FFFFFF, 2 * k), P(12, -8), P(20, -4));
        dc.FillRectangle(Brush(dark), new Rect(x - 16 * k * Math.Abs(f), y - 2 * k, 14 * k, 6 * k));
        Text(dc, "+", P(-8, 2), 16 * k, alive ? 0xFFF2F2E6 : 0xFF6A6A60, center: true, shadow: false);
        // Mast and main rotor: two blades seen edge on, their length beating as they turn.
        dc.FillRectangle(Brush(0xFF2A2A2A), new Rect(x - 2 * k, y - 19 * k, 4 * k, 7 * k));
        double a = 30 + 22 * Math.Cos(rotor * 31), b = 30 + 22 * Math.Cos(rotor * 31 + Math.PI);
        dc.DrawLine(Pen(Alpha(0xFF1A1A1A, 0.85), 2.5 * k), new Point(x - a * k, y - 19 * k), new Point(x + b * k, y - 19 * k));
        dc.DrawLine(Pen(0x30000000, 2 * k), new Point(x - 52 * k, y - 19 * k), new Point(x + 52 * k, y - 19 * k));
    }

    private static void DrawTank(DrawingContext dc, double x, double aim, double t, double vx, double k, double ground = Field.Ground)
    {
        if (x < -60 || x > Field.Width + 60) return;
        double g = ground;
        // Tracks with turning wheels.
        dc.FillRectangle(Brush(0xFF2A2A26), new Rect(x - 30 * k, g - 12 * k, 60 * k, 12 * k), (float)(6 * k));
        for (int i = 0; i < 5; i++)
        {
            double wx = x - 22 * k + i * 11 * k;
            dc.DrawEllipse(Brush(0xFF5A5A50), null, new Point(wx, g - 6 * k), 4 * k, 4 * k);
            double r = t * vx * 0.2 + i;
            dc.DrawLine(Pen(0xFF2A2A26, 1), new Point(wx - Math.Cos(r) * 3 * k, g - 6 * k - Math.Sin(r) * 3 * k), new Point(wx + Math.Cos(r) * 3 * k, g - 6 * k + Math.Sin(r) * 3 * k));
        }
        Poly(dc, 0xFF8A7A4A, 0xFF4A4026, 1.5, new Point(x - 30 * k, g - 12 * k), new Point(x - 24 * k, g - 22 * k), new Point(x + 24 * k, g - 22 * k), new Point(x + 30 * k, g - 12 * k));
        // The gun, pointing at the chopper (but never down into the ground).
        double a = Math.Clamp(aim, -Math.PI + 0.2, -0.2);
        double gx = x, gy = g - 28 * k;
        dc.DrawLine(Pen(0xFF4A4026, 4 * k), new Point(gx, gy), new Point(gx + Math.Cos(a) * 26 * k, gy + Math.Sin(a) * 26 * k));
        dc.DrawEllipse(Brush(0xFF9A8A5A), Pen(0xFF4A4026, 1.5), new Point(gx, gy + 2 * k), 13 * k, 8 * k);
        dc.FillRectangle(Brush(0xFFC03A2A), new Rect(gx - 3 * k, gy - 2 * k, 6 * k, 3 * k));
    }

    private static void DrawJet(DrawingContext dc, double x, double y, int dir, double vy, double k)
    {
        if (x < -80 || x > Field.Width + 80) return;
        double f = dir == 0 ? 1 : dir, tilt = Math.Clamp(vy / 600, -0.3, 0.3) * f;
        using var turn = dc.PushTransform(Matrix.CreateTranslation(-x, -y) * Matrix.CreateRotation(tilt) * Matrix.CreateTranslation(x, y));
        Point P(double px, double py) => new(x + px * f * k, y + py * k);
        // Flame, fuselage, swept wing, fin.
        dc.DrawEllipse(Brush(0xC0FF9A3A), null, P(-40, 0), 8 * k, 3 * k);
        Poly(dc, 0xFF9AA0A8, 0xFF4A5058, 1.2, P(38, 0), P(26, -5), P(-34, -5), P(-38, 0), P(-34, 5), P(26, 4));
        Poly(dc, 0xFF6A7078, 0xFF3A4048, 1, P(6, 2), P(-14, 12), P(-22, 12), P(-12, 2));
        Poly(dc, 0xFF6A7078, 0xFF3A4048, 1, P(-24, -4), P(-34, -18), P(-40, -18), P(-36, -4));
        Poly(dc, 0xFF3A4A5A, null, 0, P(24, -4), P(14, -8), P(8, -5));
        dc.FillRectangle(Brush(0xFFC03A2A), new Rect(x - 4 * k - (f < 0 ? 0 : 0), y - 1 * k, 8 * k, 2 * k));
    }

    private static void DrawMine(DrawingContext dc, double x, double y, double t, double k)
    {
        if (x < -30 || x > Field.Width + 30) return;
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4 + t * 0.8;
            dc.DrawLine(Pen(0xFF3A3A40, 2.5 * k), new Point(x, y), new Point(x + Math.Cos(a) * 16 * k, y + Math.Sin(a) * 16 * k));
        }
        dc.DrawEllipse(Brush(0xFF5A5A64), Pen(0xFF2A2A30, 1.5), new Point(x, y), 11 * k, 11 * k);
        dc.DrawEllipse(Brush((int)(t * 4) % 2 == 0 ? 0xFFFF3A2Au : 0xFF6A1A14), null, new Point(x, y), 3.5 * k, 3.5 * k);
    }

    private void DrawShot(DrawingContext dc, Shot s)
    {
        double x = Sx(s.X);
        if (x < -20 || x > Field.Width + 20) return;
        switch (s.Kind)
        {
            case ShotKind.Bullet:
                dc.DrawLine(Pen(0x80FFE080, 5), new Point(x - Math.Sign(s.Vx) * 10, s.Y), new Point(x, s.Y));
                dc.DrawLine(Pen(0xFFFFFFC0, 2), new Point(x - Math.Sign(s.Vx) * 8, s.Y), new Point(x, s.Y));
                break;
            case ShotKind.Bomb:
                dc.DrawEllipse(Brush(0xFF2A2A26), Pen(0xFF101010, 1), new Point(x, s.Y), 4, 6);
                break;
            case ShotKind.Shell:
                dc.DrawEllipse(Brush(0x70FF8A2A), null, new Point(x, s.Y), 7, 7);
                dc.DrawEllipse(Brush(0xFFFFD06A), null, new Point(x, s.Y), 3.5, 3.5);
                break;
            default:
                double f = Math.Sign(s.Vx);
                dc.DrawEllipse(Brush(0xA0FFAA3A), null, new Point(x - f * 14, s.Y), 6, 3);
                dc.DrawLine(Pen(0xFFE8E8E8, 4), new Point(x - f * 10, s.Y), new Point(x + f * 6, s.Y));
                dc.DrawLine(Pen(0xFFC03A2A, 4), new Point(x + f * 3, s.Y), new Point(x + f * 7, s.Y));
                break;
        }
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        if (!InGame) return;
        var c = new Point(Sx(e.X), e.Y);
        switch (e.Kind)
        {
            case EffectKind.Blast:
            {
                double r = (10 + 34 * t) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFF3A3A3A, 0.5 * (1 - t))), null, new Point(c.X, c.Y - t * 24), r * 1.1, r);
                dc.DrawEllipse(Brush(Alpha(0xFFFF8A2A, 1 - t)), null, c, r * 0.8, r * 0.7);
                dc.DrawEllipse(Brush(Alpha(0xFFFFF0A0, 1 - t)), null, c, r * 0.4, r * 0.35);
                break;
            }
            case EffectKind.Smoke:
                dc.DrawEllipse(Brush(Alpha(0xFF4A4A4A, 0.6 * (1 - t))), null, new Point(c.X, c.Y - t * 30), 6 + 10 * t, 6 + 10 * t);
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 20, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF14161E), new Rect(0, 0, Field.Width, Field.Hud));
        dc.FillRectangle(Brush(0xFF3A6A4A), new Rect(0, Field.Hud - 3, Field.Width, 3));
        Text(dc, "SCORE", new Point(16, 18), 20, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(86, 18), 30, Yellow);
        Text(dc, "HI", new Point(Field.Width - 140, 18), 20, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 18), 28, Dim, right: true);
        if (!InGame) return;
        int spare = Math.Max(0, g.Lives - (g.Player.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawChopper(dc, 28 + i * 34, 54, -1, 0, 0, true, 0.42);
        Text(dc, $"SORTIE {g.Wave}", new Point(Field.Width - 16, 52), 22, White, right: true);
        // The tally, and the radar of the whole strip.
        int waiting = g.Huts.Sum(h => h.Inside) + g.People.Count(h => h.State != HostageState.Walking);
        Text(dc, $"HOME {g.Home,2}   ABOARD {g.Player.Aboard,2}   WAITING {waiting,2}   LOST {g.Lost,2}", new Point(512, 18), 22, 0xFFB0E0C0, center: true);
        double rx = 262, rw = 500, ry = 38, rh = 30, k = rw / Field.WorldWidth;
        dc.FillRectangle(Brush(0xFF0A0E0A), new Rect(rx, ry, rw, rh));
        dc.DrawRectangle(null, Pen(0xFF3A6A4A, 1), new Rect(rx, ry, rw, rh));
        dc.DrawRectangle(null, Pen(0x60B0E0C0, 1), new Rect(rx + g.Camera * k, ry + 1, Field.Width * k, rh - 2));
        double rg = ry + rh - 4;
        dc.FillRectangle(Brush(0xFF6A8A4A), new Rect(rx + Field.Border * k, ry + 4, 1, rh - 8));
        dc.FillRectangle(Brush(0xFFF2F2E6), new Rect(rx + Field.BaseX * k - 3, rg - 2, 6, 3));
        foreach (var h in g.Huts) dc.FillRectangle(Brush(h.Open ? 0xFF6A5A4Au : 0xFFC0A060), new Rect(rx + h.X * k - 3, rg - 5, 6, 5));
        foreach (var h in g.People) dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(rx + h.X * k, rg - 3, 1.5, 3));
        foreach (var t in g.Tanks) dc.FillRectangle(Brush(Red), new Rect(rx + t.X * k - 2, rg - 2, 4, 3));
        foreach (var j in g.Jets) dc.FillRectangle(Brush(0xFFFF9A6A), new Rect(rx + j.X * k - 2, ry + 2 + (j.Y - Field.Ceiling) / (Field.Ground - Field.Ceiling) * (rh - 8), 4, 2));
        foreach (var m in g.Mines) dc.FillRectangle(Brush(0xFFFF6AE0), new Rect(rx + m.X * k - 1, ry + 2 + (m.Y - Field.Ceiling) / (Field.Ground - Field.Ceiling) * (rh - 8), 3, 3));
        var p = g.Player;
        double py = ry + 2 + Math.Clamp((p.Y - Field.Ceiling) / (Field.Ground - Field.Ceiling), 0, 1) * (rh - 8);
        dc.FillRectangle(Brush((int)(_clock * 4) % 2 == 0 ? 0xFF7AF0A0u : 0xFF3A8A5Au), new Rect(rx + p.X * k - 2, py, 5, 4));
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 250;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xC014181E), new Rect(cx - w / 2, cy - 46, w, h), 6);
        dc.DrawRectangle(null, Pen(0xFF5A9A6A, 1.5), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawLand(dc, 2600 + Math.Sin(_clock * 0.1) * 300);
        dc.FillRectangle(Brush(0x60000010), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        string title = "AVA CHOPLIFTER";
        for (int i = 0; i < title.Length; i++)
        {
            double bob = Math.Sin(_clock * 3 + i * 0.5) * 4;
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 24 + 24 + i * 48, 132 + bob), 100, i % 2 == 0 ? 0xFFFFD27A : 0xFFFFF0C0, center: true);
        }
        Text(dc, "AFTER THE 1985 ARCADE CLASSIC", new Point(cx, 194), 28, 0xFFF2E0C0, center: true);
        // The chopper, swaying over a line of hostages waving it in.
        double hx = cx + Math.Sin(_clock * 0.7) * 200;
        DrawChopper(dc, hx, 268 + Math.Sin(_clock * 1.3) * 8, Math.Cos(_clock * 0.7) > 0 ? 1 : -1, Math.Cos(_clock * 0.7) * 0.15, _clock, true, 1.6);
        for (int i = 0; i < 7; i++) DrawHostage(dc, cx - 150 + i * 50, 352, _clock + i, false, true, 0, 1.6);
        Text(dc, "SHOOT OPEN THE BARRACKS - LAND - FLY THE HOSTAGES HOME TO THE PAD", new Point(cx, 380), 24, White, center: true);
        (string Name, string Points, Action<double> Draw)[] legend =
        [
            ("TANK", GameEngine.TankPoints.ToString(), x => DrawTank(dc, x, -Math.PI / 2 + 0.6, _clock, 0, 1.2, 466)),
            ("JET", GameEngine.JetPoints.ToString(), x => DrawJet(dc, x, 444, -1, 0, 1.1)),
            ("AIR MINE", GameEngine.MinePoints.ToString(), x => DrawMine(dc, x, 444, _clock, 1.2)),
            ("HOSTAGE HOME", GameEngine.RescuePoints.ToString(), x => DrawHostage(dc, x, 466, _clock, true, false, 1, 1.8)),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double lx = cx - 330 + i * 220;
            legend[i].Draw(lx);
            Text(dc, legend[i].Name, new Point(lx, 492), 22, 0xFFF2E0C0, center: true);
            Text(dc, legend[i].Points, new Point(lx, 516), 22, Yellow, center: true);
        }
        Text(dc, "16 ABOARD AT MOST - DON'T LAND ON ANYONE - FACE THE FRONT TO BOMB TANKS", new Point(cx, 552), 21, 0xFFF2E0C0, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1 TO PLAY", new Point(cx, 608), 46, Yellow, center: true);
        Text(dc, "ARROWS: FLY (DOWN TO LAND, UP TO LIFT OFF)    SPACE: FIRE    X OR SHIFT: FACE THE FRONT", new Point(cx, 662), 21, 0xFFF2E0C0, center: true);
        Text(dc, $"P: PAUSE    M: SOUND    EXTRA CHOPPER EVERY {GameEngine.ExtraLifeEvery}    GAMEPAD: STICK, A, B", new Point(cx, 690), 21, 0xFFF2E0C0, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF161A22), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "THE TOP PILOTS", new Point(cx, 140), 68, 0xFFFFD27A, center: true);
        Text(dc, "RANK NAME    SCORE  SORTIE", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF161A22), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
