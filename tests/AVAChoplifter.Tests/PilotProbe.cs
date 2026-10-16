using AVAChoplifter.Engine;

namespace AVAChoplifter.Tests;

/// <summary>How the computer player gets on, sortie by sortie (set CHOPLIFTER_PROBE to run it, for tuning: "1" for the pilot, "lazy" for one that sweeps back and forth across the strip, weaving, firing all the time).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("CHOPLIFTER_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "choplifter-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var levels = new List<string>();
            var doing = new Dictionary<string, int>();
            var losses = new Dictionary<string, int>();
            var last = g.Phase;
            int wave = 1, t = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 10 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Dx: (t / 900) % 2 == 0 ? -1 : 1, Dy: (t / 50) % 3 - 1, Fire: true, Front: (t / 200) % 4 == 0) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"L{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"L{g.Wave} {g.Cause}") + 1;
                if (g.Phase == Phase.Clear && last != Phase.Clear) levels.Add($"L{wave}:{(t - start) / 60:0}s {g.Home}/{g.Home + g.Lost}");
                if (g.Wave != wave) { wave = g.Wave; start = t; }
                foreach (var e in g.Effects.Where(e => e.Kind == EffectKind.Word && e.Age == 0)) losses[$"L{g.Wave} {e.Text}"] = losses.GetValueOrDefault($"L{g.Wave} {e.Text}") + 1;
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: sortie {g.Wave} after {t / 60:0}s score {g.Score} rescued {g.Rescued} (now {g.Home} home {g.Lost} lost {g.Unaccounted} to go) | {string.Join(" ", levels)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderBy(d => d.Key).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   hostages lost " + string.Join(", ", losses.OrderBy(d => d.Key).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
