using AVAChoplifter.Engine;

namespace AVAChoplifter.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avachoplifter-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Sortie 1 in play, the chopper on the pad; calm (the enemy put there stays put and holds its fire) unless <paramref name="calm"/> is false.</summary>
    private static GameEngine Game(bool calm = true, int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = calm };
        g.Go(wave);
        return g;
    }

    /// <summary>The chopper hovering at a height, facing a way, with no barracks about.</summary>
    private static GameEngine Hovering(double x = 2000, double y = 400, int facing = -1, bool calm = true)
    {
        var g = Game(calm);
        ClearHuts(g);
        g.PutChopper(x, y, facing);
        return g;
    }

    /// <summary>The barracks out of the way: just one, far off at the left end, so the sortie isn't over.</summary>
    private static void ClearHuts(GameEngine g)
    {
        g.Huts.Clear();
        g.Huts.Add(new Barracks { X = 150, Inside = 8, Held = 8 });
    }

    private static Hostage PutHostage(GameEngine g, double x)
    {
        var h = new Hostage { X = x, Home = x };
        g.People.Add(h);
        return h;
    }

    private static readonly Controls FireButton = new(Fire: true), Up = new(0, -1), Down = new(0, 1);

    // ───────────────────────────── the strip ─────────────────────────────

    [Fact]
    public void Every_sortie_has_four_barracks_of_hostages_spread_out_on_the_enemy_side_of_the_border()
    {
        for (int wave = 1; wave <= 8; wave++)
        {
            var a = Game(wave: wave, seed: 1);
            var b = Game(wave: wave, seed: 77);
            Assert.Equal(a.Huts.Select(h => h.X), b.Huts.Select(h => h.X));
            Assert.Equal(4, a.Huts.Count);
            Assert.All(a.Huts, h => Assert.Equal((GameEngine.PerBarracks, false), (h.Inside, h.Open)));
            Assert.All(a.Huts, h => Assert.True(h.X < Field.Border - 300));
            var xs = a.Huts.Select(h => h.X).OrderBy(x => x).ToList();
            for (int i = 1; i < xs.Count; i++) Assert.True(xs[i] - xs[i - 1] > 600);
            Assert.Equal(32, a.Unaccounted);
        }
    }

    [Fact]
    public void Later_sorties_bring_more_and_faster_tanks_jets_more_often_and_air_mines_from_the_third()
    {
        var g = Game();
        var seen = new List<(int Tanks, double Speed, double JetGap, int Mines)>();
        foreach (int w in new[] { 1, 2, 3, 5, 8 })
        {
            g.Go(w);
            seen.Add((g.TankLimit, g.TankSpeed, g.JetGap, g.MineLimit));
        }
        for (int i = 1; i < seen.Count; i++)
        {
            Assert.True(seen[i].Tanks >= seen[i - 1].Tanks);
            Assert.True(seen[i].Speed > seen[i - 1].Speed);
            Assert.True(seen[i].JetGap < seen[i - 1].JetGap);
        }
        Assert.Equal([0, 0, 1, 3, 4], seen.Select(s => s.Mines));
        // Tanks stay slower than a running hostage.
        Assert.True(seen[^1].Speed < GameEngine.RunSpeed);
    }

    // ───────────────────────────── flying ─────────────────────────────

    [Fact]
    public void The_chopper_starts_on_the_pad_and_lifts_off_with_up()
    {
        var g = Game();
        Assert.True(g.Player.Landed);
        Assert.True(GameEngine.AtBase(g.Player.X));
        g.Run(30, new Controls(-1, 0));
        Assert.True(g.Player.Landed);
        Assert.Equal(Field.BaseX, g.Player.X);
        g.Run(30, Up);
        Assert.False(g.Player.Landed);
        Assert.Equal(Chopper.LandedY - GameEngine.Climb * 0.5, g.Player.Y, 3);
    }

    [Fact]
    public void Across_it_gathers_speed_and_coasts_to_a_stop_up_and_down_it_goes_straight_away()
    {
        var g = Hovering();
        g.Run(30, new Controls(1, 0));
        Assert.Equal(GameEngine.Accel * 0.5, g.Player.Vx, 1);
        g.Run(60, new Controls(1, 0));
        Assert.Equal(GameEngine.MaxSpeed, g.Player.Vx, 1);
        g.Run(30, default);
        Assert.True(g.Player.Vx > 0 && g.Player.Vx < GameEngine.MaxSpeed);
        g.Run(60, default);
        Assert.Equal(0, g.Player.Vx, 1);
        double y = g.Player.Y;
        g.Run(1, Up);
        Assert.Equal(y - GameEngine.Climb / 60, g.Player.Y, 3);
    }

    [Fact]
    public void Coming_down_slowly_it_lands_and_too_fast_it_crashes()
    {
        var g = Hovering(y: Chopper.LandedY - 30);
        g.Run(20, Down);
        Assert.True(g.Player.Landed);
        Assert.Equal(Phase.Play, g.Phase);

        g = Hovering(y: Chopper.LandedY - 30);
        g.Run(60, new Controls(1, 0));
        g.Run(20, new Controls(1, 1));
        Assert.Equal((Phase.Dying, "crashed"), (g.Phase, g.Cause));
    }

    [Fact]
    public void It_faces_the_way_it_is_pushed_and_the_front_while_that_is_held()
    {
        var g = Hovering(facing: -1);
        g.Run(1, new Controls(1, 0));
        Assert.Equal(1, g.Player.Facing);
        g.Run(1, new Controls(Front: true));
        Assert.Equal(0, g.Player.Facing);
        g.Run(1, new Controls(-1, 0, Front: true));
        Assert.Equal(0, g.Player.Facing);
        g.Run(1, default);
        Assert.Equal(-1, g.Player.Facing);
    }

    [Fact]
    public void Facing_sideways_it_fires_along_three_at_a_time_and_facing_the_front_it_drops_bombs()
    {
        var g = Hovering(facing: 1);
        g.Run(1, FireButton);
        var b = Assert.Single(g.Shots);
        Assert.Equal((ShotKind.Bullet, GameEngine.BulletSpeed, 0.0), (b.Kind, b.Vx, b.Vy));
        g.Run(40, FireButton);
        Assert.Equal(3, g.Shots.Count(s => s.Kind == ShotKind.Bullet));

        g = Hovering();
        g.Run(1, new Controls(Front: true));
        g.Run(15, new Controls(Fire: true, Front: true));
        var bomb = Assert.Single(g.Shots);
        Assert.Equal(ShotKind.Bomb, bomb.Kind);
        double y = bomb.Y;
        g.Run(20, new Controls(Front: true));
        Assert.True(bomb.Y > y + 40 && bomb.Vy > GameEngine.BombDrop);
    }

    [Fact]
    public void Turning_takes_a_moment_before_it_can_fire_again()
    {
        var g = Hovering(facing: -1);
        g.Run(1, new Controls(1, 0, true));
        Assert.Empty(g.Shots);
        g.Run((int)(GameEngine.TurnSeconds * 60) + 1, new Controls(0, 0, true));
        Assert.Single(g.Shots);
    }

    // ───────────────────────────── the hostages ─────────────────────────────

    [Fact]
    public void A_barracks_takes_three_hits_then_its_hostages_come_out_one_at_a_time_and_the_enemy_wakes()
    {
        var g = Game();
        var hut = g.Huts[0];
        g.PutChopper(hut.X + 200, Field.Ground - 34, -1);
        Assert.False(g.Alert);
        for (int i = 0; i < 2; i++) { g.Run(1, FireButton); g.Run(40, default); }
        Assert.Equal((2, false), (hut.Hits, hut.Open));
        g.Run(1, FireButton);
        g.Run(40, default);
        Assert.True(hut.Open);
        Assert.True(g.Alert);
        Assert.InRange(g.People.Count, 1, 3);
        g.Run(60 * 4, default);
        Assert.Equal(GameEngine.PerBarracks, g.People.Count);
        Assert.Equal(0, hut.Inside);
        Assert.All(g.People, h => Assert.True(Math.Abs(h.X - hut.X) < 120));
    }

    [Fact]
    public void Hostages_run_for_the_chopper_once_it_lands_near_and_climb_aboard_sixteen_at_most()
    {
        var g = Game();
        ClearHuts(g);
        for (int i = 0; i < 20; i++) PutHostage(g, 1800 + i * 10);
        g.PutChopper(2100, Chopper.LandedY);
        g.Run(60 * 8, default);
        Assert.Equal(Chopper.Capacity, g.Player.Aboard);
        Assert.Equal(4, g.People.Count);
        Assert.All(g.People, h => Assert.Equal(HostageState.Waiting, h.State));
    }

    [Fact]
    public void Taking_off_leaves_those_still_running_to_wait_again()
    {
        var g = Game();
        ClearHuts(g);
        var h = PutHostage(g, 1700);
        g.PutChopper(2100, Chopper.LandedY);
        g.Run(30, default);
        Assert.Equal(HostageState.Running, h.State);
        g.Run(10, Up);
        g.Run(2, default);
        Assert.Equal(HostageState.Waiting, h.State);
        Assert.Equal(0, g.Player.Aboard);
    }

    [Fact]
    public void Hostages_far_off_stay_put()
    {
        var g = Game();
        ClearHuts(g);
        var h = PutHostage(g, 2100 - GameEngine.CallRange - 100);
        g.PutChopper(2100, Chopper.LandedY);
        g.Run(60, default);
        Assert.Equal(HostageState.Waiting, h.State);
    }

    [Fact]
    public void Landing_on_a_hostage_a_bomb_beside_one_or_a_bullet_low_down_loses_them()
    {
        var g = Hovering(y: Chopper.LandedY - 20);
        PutHostage(g, 2000);
        g.Run(20, Down);
        Assert.Equal(1, g.Lost);

        g = Hovering();
        PutHostage(g, 2020);
        g.Run(1, new Controls(Front: true));
        g.Run(12, new Controls(Fire: true, Front: true));
        g.Run(90, new Controls(Front: true));
        Assert.Equal(1, g.Lost);

        g = Hovering(y: Chopper.LandedY, facing: 1);
        PutHostage(g, 2200);
        g.Run(1, FireButton);
        g.Run(30, default);
        Assert.Equal(1, g.Lost);
    }

    [Fact]
    public void Set_down_on_the_pad_they_get_off_and_walk_in_for_five_hundred_each()
    {
        var g = Game();
        ClearHuts(g);
        g.Player.Aboard = 3;
        g.Run(60 * 6, default);
        Assert.Equal(0, g.Player.Aboard);
        Assert.Equal((3, 3, 3 * GameEngine.RescuePoints), (g.Home, g.Rescued, g.Score));
        Assert.Empty(g.People);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    [Fact]
    public void Nothing_comes_until_a_barracks_is_shot_open_or_a_while_has_gone_by()
    {
        var g = Game(calm: false);
        g.PutChopper(2500, 400);
        g.Run((int)(GameEngine.AlertSeconds * 60) - 10, default);
        Assert.False(g.Alert);
        Assert.Empty(g.Tanks);
        Assert.Empty(g.Jets);
        g.Run(60 * 12, default);
        Assert.True(g.Alert);
        Assert.True(g.Tanks.Count + g.Jets.Count >= 1);
    }

    [Fact]
    public void A_tank_rolls_under_the_chopper_fires_up_at_it_and_a_hit_brings_it_down_with_everyone_aboard()
    {
        var g = Hovering(calm: false, y: 500);
        g.Player.Aboard = 5;
        var t = g.PutTank(1700);
        g.Run(20, default);
        Assert.True(t.X > 1700);
        var shell = Assert.Single(g.Shots);
        Assert.True(shell.Vy < 0 && shell.Vx > 0);
        int lives = g.Lives;
        for (int i = 0; i < 120 && g.Phase == Phase.Play; i++) g.Tick(default);
        Assert.Equal((Phase.Dying, "shot down", lives - 1, 5, 0), (g.Phase, g.Cause, g.Lives, g.Lost, g.Player.Aboard));
        g.Run((int)(GameEngine.DyingSeconds * 60) + 2, default);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.True(g.Player.Landed && GameEngine.AtBase(g.Player.X));
    }

    [Fact]
    public void A_bomb_or_a_bullet_low_down_knocks_out_a_tank()
    {
        var g = Hovering(y: 500);
        g.PutTank(2000);
        g.Run(1, new Controls(Front: true));
        g.Run(12, new Controls(Fire: true, Front: true));
        g.Run(90, new Controls(Front: true));
        Assert.Empty(g.Tanks);
        Assert.Equal(GameEngine.TankPoints, g.Score);

        g = Hovering(y: Chopper.LandedY - 6, facing: -1);
        g.PutTank(1800);
        g.Run(1, FireButton);
        g.Run(30, default);
        Assert.Empty(g.Tanks);
    }

    [Fact]
    public void Tanks_keep_this_side_of_the_border_and_hold_fire_at_the_base()
    {
        var g = Game(calm: false);
        g.SetAlert(true);
        var t = g.PutTank(Field.Border - 300);
        g.Run(60 * 10, default);
        Assert.True(t.X < Field.Border);
        Assert.DoesNotContain(g.Shots, s => s.Kind == ShotKind.Shell);
    }

    [Fact]
    public void Hostages_run_from_a_tank_coming_at_them_and_one_that_catches_them_runs_them_over()
    {
        var g = Hovering(calm: false, x: 2400, y: 500);
        var h = PutHostage(g, 2200);
        g.PutTank(2060);
        g.Run(30, default);
        Assert.True(h.X > 2200);

        g = Hovering(calm: false, x: 2400, y: 500);
        PutHostage(g, 2075);
        g.PutTank(2060);
        g.Run(10, default);
        Assert.Equal(1, g.Lost);
    }

    [Fact]
    public void A_jet_comes_down_to_the_chopper_fires_a_missile_and_can_be_shot_down()
    {
        var g = Hovering(calm: false, y: 450, facing: -1);
        var j = g.PutJet(1300, 200, GameEngine.JetSpeed);
        for (int i = 0; i < 120 && !g.Shots.Any(s => s.Kind == ShotKind.Missile); i++) g.Tick(default);
        var m = Assert.Single(g.Shots, s => s.Kind == ShotKind.Missile);
        Assert.True(Math.Abs(j.Y - 450) < 40 && m.Vx > 0);

        g = Hovering(y: 400, facing: -1);
        g.PutJet(1700, 404, 0);
        g.Run(1, FireButton);
        g.Run(40, default);
        Assert.Empty(g.Jets);
        Assert.Equal(GameEngine.JetPoints, g.Score);
    }

    [Fact]
    public void Flying_into_a_jet_or_a_mine_brings_the_chopper_down()
    {
        var g = Hovering(y: 400);
        g.PutJet(2040, 400, 0);
        g.Tick(default);
        Assert.Equal((Phase.Dying, "hit by a jet"), (g.Phase, g.Cause));

        g = Hovering(y: 400);
        g.PutMine(2030, 405);
        g.Tick(default);
        Assert.Equal((Phase.Dying, "hit a mine"), (g.Phase, g.Cause));
    }

    [Fact]
    public void An_air_mine_drifts_after_the_chopper_and_a_bullet_finishes_it()
    {
        var g = Hovering(calm: false, y: 400);
        var m = g.PutMine(1600, 200);
        double d = Field.Distance(m.X, m.Y, 2000, 400);
        g.Run(60, default);
        Assert.True(Field.Distance(m.X, m.Y, g.Player.X, g.Player.Y) < d - 20);

        g = Hovering(y: 400, facing: -1);
        g.PutMine(1700, 404);
        g.Run(1, FireButton);
        g.Run(40, default);
        Assert.Empty(g.Mines);
        Assert.Equal(GameEngine.MinePoints, g.Score);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void With_everyone_home_or_lost_the_sortie_ends_with_a_bonus_and_more_if_none_were_lost()
    {
        var g = Game(wave: 2);
        foreach (var h in g.Huts) h.Inside = 0;
        g.Player.Aboard = 4;
        for (int i = 0; i < 60 * 8 && g.Phase != Phase.Clear; i++) g.Tick(default);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(GameEngine.HomeBonus * 4 * 2 + GameEngine.SafeBonus, g.Bonus);
        int before = g.Score;
        g.Run((int)(GameEngine.ClearSeconds * 60) + 2, default);
        Assert.Equal(before + g.Bonus, g.Score);
        Assert.Equal(3, g.Wave);
        Assert.Equal(32, g.Unaccounted);
    }

    [Fact]
    public void Every_twenty_thousand_is_an_extra_chopper()
    {
        var g = Game();
        ClearHuts(g);
        g.SetScore(GameEngine.ExtraLifeEvery - 200);
        int lives = g.Lives;
        g.Player.Aboard = 1;
        g.Run(60 * 4, default);
        Assert.Equal(lives + 1, g.Lives);
    }

    [Fact]
    public void The_last_chopper_lost_ends_the_game_and_a_high_score_asks_for_initials()
    {
        var g = Hovering(y: 400);
        g.SetLives(1);
        g.SetScore(99999);
        g.PutJet(2040, 400, 0);
        g.Idle(1);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "CHP") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("CHP", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_and_the_demos_vary_the_sortie()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var waves = new List<int>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            waves.Add(g.Wave);
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal([1, 2], waves);
    }

    [Fact]
    public void Enter_starts_a_game_on_the_pad()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal((1, 3, 0, Phase.Start), (g.Wave, g.Lives, g.Score, g.Phase));
        Assert.True(g.Player.Landed && GameEngine.AtBase(g.Player.X));
    }

    [Fact]
    public void The_up_key_lifts_the_chopper_and_the_down_key_brings_it_down()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Avalonia.Input.Key.Up);
        Assert.Equal(-1, input.Controls.Dy);
        var g = Game();
        g.Run(30, input.Controls);
        Assert.True(g.Player.Y < Chopper.LandedY - 80);
        input.OnKeyUp(Avalonia.Input.Key.Up);
        input.OnKeyDown(Avalonia.Input.Key.Down);
        Assert.Equal(1, input.Controls.Dy);
        g.Run(60, input.Controls);
        Assert.True(g.Player.Landed);
    }

    [Fact]
    public void The_computer_player_brings_most_of_the_first_sortie_home()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(1);
            for (int i = 0; i < 60 * 240 && g.Wave == 1 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
            Assert.True(g.Wave == 2, $"seed {seed}: still out on sortie 1");
            Assert.True(g.Rescued >= 24, $"seed {seed}: only {g.Rescued} home");
        }
    }

    [Fact]
    public void Flying_about_firing_blindly_brings_nobody_home()
    {
        var g = new GameEngine(2, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        for (int t = 0; t < 60 * 180 && g.State == GameState.Playing; t++)
            g.Tick(new Controls((t / 900) % 2 == 0 ? -1 : 1, (t / 50) % 3 - 1, true, (t / 200) % 4 == 0));
        Assert.Equal(0, g.Rescued);
    }
}
