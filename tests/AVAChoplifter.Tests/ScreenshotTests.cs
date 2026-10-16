using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAChoplifter.Engine;
using AVAChoplifter.Views;
using Controls = AVAChoplifter.Engine.Controls;

namespace AVAChoplifter.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set CHOPLIFTER_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avachoplifter-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
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
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("CHOPLIFTER_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAChoplifter-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("CHP", 96400, 6);
        g.HighScores.Insert("AVA", 61200, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A sortie, and the computer at work in it until something worth showing.</summary>
    private static void Fly(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 300)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, busy, seconds));
        Shot(w, c, name);
    }

    private static bool Flying(GameEngine g) => g.Phase == Phase.Play && g.Player.Alive && g.Player.Safe <= 0;

    [AvaloniaFact]
    public void Shooting_open_a_barracks() => Fly(1, "barracks", g => Flying(g) && g.Pilot.Doing == "shooting the barracks" && g.Shots.Any(s => s.Kind == ShotKind.Bullet) && g.Huts.Any(h => h.Hits >= 1 && !h.Open));

    [AvaloniaFact]
    public void Hostages_running_aboard() => Fly(1, "boarding", g => Flying(g) && g.Player.Landed && g.People.Count(h => h.State == HostageState.Running) >= 5 && g.Player.Aboard >= 2);

    [AvaloniaFact]
    public void Bombing_a_tank() => Fly(2, "tank", g => Flying(g) && g.Player.Facing == 0 && g.Shots.Any(s => s.Kind == ShotKind.Bomb && s.Y > g.Player.Y + 60) && g.Tanks.Any(t => Math.Abs(t.X - g.Player.X) < 200));

    [AvaloniaFact]
    public void A_jet() => Fly(3, "jet", g => Flying(g) && g.Jets.Any(j => j.X > g.Camera + 100 && j.X < g.Camera + Field.Width - 100) && (g.Shots.Any(s => s.Kind == ShotKind.Missile) || g.Shots.Any(s => s.Kind == ShotKind.Shell)), seconds: 400);

    [AvaloniaFact]
    public void Air_mines() => Fly(4, "mines", g => Flying(g) && g.Mines.Any(m => m.X > g.Camera + 50 && m.X < g.Camera + Field.Width - 50 && Math.Abs(m.X - g.Player.X) < 400) && g.Jets.Any(j => j.X > g.Camera && j.X < g.Camera + Field.Width) && !g.Player.Landed, seconds: 400);

    [AvaloniaFact]
    public void Home_to_the_pad() => Fly(1, "home", g => Flying(g) && g.Player.Landed && GameEngine.AtBase(g.Player.X) && g.People.Count(h => h.State == HostageState.Walking) >= 5, seconds: 300);

    [AvaloniaFact]
    public void Shot_down() => Fly(5, "shot-down", g => g.Phase == Phase.Dying && g.PhaseTime is > 0.3 and < 0.6 && g.Player.Y < Field.Ground - 100, seconds: 900);
}
