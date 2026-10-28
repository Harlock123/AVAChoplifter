# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A helicopter rescue game in the style of Sega's 1985 arcade game Choplifter, in C# on .NET 10 and Avalonia 12,
drawn through Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM).
Everything in it is original: the graphics are drawn in code, and the sounds and tunes are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAChoplifter.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAChoplifter.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVAChoplifter          # play it; --no-audio for silence
CHOPLIFTER_SHOTS=$PWD/Images dotnet test tests/AVAChoplifter.Tests --filter ScreenshotTests   # refresh the README's pictures
CHOPLIFTER_PROBE=1 dotnet test tests/AVAChoplifter.Tests --filter PilotProbe --logger "console;verbosity=detailed"   # tuning probe
python3 tools/make_sounds.py                    # rebuild src/AVAChoplifter/Sounds
```

`CHOPLIFTER_PROBE=lazy` runs the probe with a pilot that just sweeps the strip firing. `Directory.Build.props`
sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every project: a warning fails
the build, so keep it at zero.

## Layout

- `src/AVAChoplifter/Engine/`: the game, with no Avalonia drawing in it.
  - `Entities.cs` has `Controls`, the `Field` (measures and the 1/60 s `Step`) and the chopper, barracks,
    hostages, tanks, jets, mines, shots and effects.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `GameEngine.Play.cs` plays a sortie: laying out the barracks, the hostages, the enemy and the scoring.
  - `ChopperPilot.cs` is the computer player, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAChoplifter/Views/`: `GameCanvas` draws the fixed 1024x768 picture, scaled to the window, and runs the
  60 Hz timer; `GameCanvas.Draw.cs` holds the drawing.
- `src/AVAChoplifter/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAChoplifter/Sounds/`: the generated WAV files.
- `tests/AVAChoplifter.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: `GameplayTests`, `ScreenshotTests`,
  `ShiftReleaseTests` and the opt-in `PilotProbe`.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` feeds frame time into an accumulator and runs the game in fixed
  `Field.Step` (1/60 s) steps, so tests can play sorties deterministically from a seed. Draw random numbers from
  the engine's seeded `_rng` (`new Random(seed)`), never from `Random.Shared`. Each sortie's layout comes from
  its own fixed seed in `Build`, so it is nearly the same every time.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** Up/down on keys, D-pad and stick must produce the same `Dy` sign the
  engine expects (negative is up the screen). Test new controls through `InputHandler`, not by building
  `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAChoplifter`); tests
  load the table from a temporary folder and run with `Silent = true`. `Calm = true` keeps the enemy away unless
  a test places it.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
After a release it asks the portfolio site to refresh, if the `SITE_REFRESH_TOKEN` secret is set.
