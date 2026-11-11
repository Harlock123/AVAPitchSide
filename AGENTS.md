# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

Top-down association football, eleven a side, for one player against the computer or two at one keyboard, in C#
on .NET 10 and Avalonia 12, drawn with Avalonia's `DrawingContext`. An original game in the genre: its clubs,
players and strips are invented. One self-contained executable per platform (Windows, macOS and Linux, x64 and
ARM). The players are small sprites drawn in code, and the sounds and music are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAPitchSide.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAPitchSide.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVAPitchSide          # play it; --no-audio for silence
PITCH_SHOTS=$PWD/Images dotnet test tests/AVAPitchSide.Tests --filter ScreenshotTests   # refresh the README's pictures
python3 tools/make_sounds.py              # rebuild src/AVAPitchSide/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAPitchSide/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, team select, demonstration, play, high scores), the one-player cup
    and the two-player friendly, the cards around a match (intro, half time, full time), goal replays (it
    records a `Frame` a step), scoring and the fixed-step loop.
  - `Match.cs` is one match, a step at a time: `Player` and `Ball`, a person's controls (passing, shooting,
    aftertouch, slides and headers), kicks (`Strike`, `Pass`, `Lob`, `Header`), touching the ball (traps,
    tackles, fouls, keepers' catches and parries), the lines, the restarts, the clock and the shoot-out.
  - `Match.Ai.cs` is the computer's side of `Match`: formation places, who chases, the man on the ball,
    keepers, and taking restarts.
  - `Pitch.cs` holds `Vec` and the pitch's measurements; `Teams.cs` the eight clubs.
  - `InputHandler.cs` (two sets of keys) and `ControllerManager.cs` (two SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAPitchSide/Views/`: `GameCanvas` (split over partial files) draws the 1024x768 frame, scaled to the
  window, and runs the 60 Hz timer. `GameCanvas.Draw.cs` draws the pitch, the goals, the stands, the players and
  the ball, through a camera that follows the ball; `GameCanvas.Screens.cs` the bar, the radar and the screens.
- `src/AVAPitchSide/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAPitchSide.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay tests, and screenshot tests that
  draw the game in a headless window.
- `tools/make_sounds.py` writes the sounds.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` turns frame time into fixed 1/60 s steps and `Match.Tick` advances the
  match by one. The match's `Random` is passed in, seeded in tests, so a test match plays the same every run.
- **Space.** Pitch units are 12 to the metre: the touchlines are x = 0 and `Pitch.Width`, the goal lines y = 0
  (top) and `Pitch.Length` (bottom), and y grows down the screen. Height is `Ball.Z`, drawn as a lift up the
  screen (`Lift`). The home side (0) attacks the top goal in the first half; `Match.Dir`, `AttackGoalY` and
  `OwnGoalY` say which way each side goes, and the ends swap at half time. In a shoot-out everyone shoots at the
  top goal. Formation places are given as across and depth for a side attacking up, turned round by
  `Match.Place`.
- **Up means up.** `Controls.Y` is +1 for up the screen, which is −y on the pitch (`Controls.Direction`). Test
  controls through `InputHandler`, not by building `Controls` alone.
- **One way to kick.** Every kick, header, throw and keeper's clearance goes through `Match.Strike`, which sets
  who touched the ball last (for throw-ins, corners and goal kicks), starts play after a restart and lets the
  kicker's aftertouch run. `Pass` and `Lob` name the team-mate it's for (`Ball.Target`), who goes to meet it.
- **Rolling friction is exponential.** A rolling ball's speed falls as e^(−`Friction`·t), so it rolls v/Friction:
  `Pass` relies on that to judge its pace, and a test holds it.
- **The computer plays by the same rules.** Its players kick with the same `Strike`, tackle with the same slide,
  and can be fouled the same way; the keeper is always the computer's, for both sides.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game and draws it. The camera eases by the drawing clock, not per frame drawn, so the
  screenshot tests advance the clock before they capture.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game and draws it.
- **Input goes through `InputHandler`.** Keys and the gamepad stick must agree, and up must mean up. Test new
  controls through `InputHandler`, not by setting `Controls` directly.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder
  (`SpecialFolder.ApplicationData`, `AVAPitchSide`); tests load the table from a fresh temporary folder and run with
  `Silent = true`.
- **Line endings.** The sources are LF; `app.manifest` is CRLF. Keep each file's existing line endings so diffs
  show only real changes.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
A release also asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
