# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls,
scoring and sounds; this file covers how the code is put together and the rules that keep it working.

## What this is

A recreation of Sega's 1983 vector arcade game Star Trek: Strategic Operations Simulator, in C# on .NET 10 and
Avalonia 12. The cabinet's split screen: a status panel and a first-person main viewer on top, the overhead
tactical scanner below. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM).

This repo is older than most of the AVA games and is laid out differently: a single project at the repo root, no
`src/` or `tests/` folders, no `global.json` and no `Directory.Build.props`.

## Commands

```bash
dotnet build AVATrek.slnx                 # one project, AVATrek.csproj, targeting net10.0
dotnet run --project AVATrek.csproj       # play it (no command-line options)
python3 tools/make_sounds.py              # rebuild the sound effects in Sounds/
tools/make_voice.sh                       # rebuild Sounds/Voice*.wav (needs Piper; see below)
```

There are no tests yet. `make_voice.sh` speaks the ship's reports with the Piper text-to-speech engine and the
`en_GB-vctk-medium` voice (paths overridable with `PIPER`, `VOICE` and `SPEAKER`). It then runs each one through
`tools/crunch_voice.py` for the speech-chip sound.

## Layout

- `Engine/`: the game logic.
  - `GameEngine.cs` runs everything: attract mode, rounds (briefing, fight, tally), the Enterprise's systems,
    enemies, docking and scoring.
  - `Rounds.cs` plans each sector's six rounds; `AttractPilot.cs` flies the ship in attract mode and defines
    `ShipControls`.
  - `InputHandler.cs` reads keys, mouse buttons, the wheel, the mouse "spinner" and the gamepad
    (`ControllerManager.cs`, SDL2).
  - `MouseWarp.cs` recentres the pointer for spinner mode, calling each platform directly (Windows `user32`,
    X11, macOS CoreGraphics). It does nothing where none of those works.
  - `SoundManager.cs` plays the effects and the voice through NetCoreAudio; `Settings.cs`, `HighScoreTable.cs`
    and `InitialsEntry.cs` do what their names say.
- `Models/`: `World` (the sector, which wraps at its edges), `GameObject`, `Enterprise`, `Enemies`,
  `Projectiles` (also starbases), `Explosion`, and `Wireframe` (3D line models and `Vec3`).
- `Views/`: `GameCanvas` is the fixed 1024x768 screen, scaled to the window, and runs the game timer.
  `GameCanvas.Screens.cs` draws the status panel and scanner, `GameCanvas.Viewer.cs` the 3D main viewer, and
  `GameCanvas.Draw.cs` the glow lines and models.
- `Sounds/`: the WAV files, copied to the output. `tools/`: the sound and voice generators.

## Rules that keep it working

- **Variable time step.** A `DispatcherTimer` ticks about every 16 ms. `GameCanvas` passes the real elapsed time,
  capped at 0.1 s, to `GameEngine.Update(deltaTime)`. Movement must scale by `deltaTime`. There is no fixed-step
  loop and no seeded random source: `AttractPilot` and `Explosion` use unseeded `new Random()`.
- **The sector wraps.** Positions go through `World.Wrap`, and every distance and bearing is taken the short
  way round. Anything drawn near an edge is drawn again across the wrap.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. Keep new SDL
  calls inside it.
- **Audio.** NetCoreAudio plays the WAV files. On Linux it needs `aplay` (`alsa-utils`); without it the game
  runs silent.
- **Saved files.** High scores (`highscores.txt`) and settings (`settings.txt`, spinner mode and sensitivity)
  live in an `AVATrek` folder under `SpecialFolder.ApplicationData`. Screenshots go to `MyPictures`.
- **Warnings.** Nullable reference types are on, but warnings are not treated as errors here.
- **Line endings.** Keep each file's existing line endings (`App.axaml`, `App.axaml.cs`, `MainWindow.axaml.cs`,
  `Program.cs`, `app.manifest` and `Engine/ControllerManager.cs` are CRLF) so diffs show only real changes.

## Making a change

- Build with no new warnings, and play the change through, since there are no tests to catch a regression.
- Update `README.md` (features, controls, scoring, the project structure) to match.
- New sounds go into `tools/make_sounds.py` or `tools/make_voice.sh`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds every push (there is no test step). It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. After a release it asks the portfolio site to refresh, if the `SITE_REFRESH_TOKEN` secret is set.
