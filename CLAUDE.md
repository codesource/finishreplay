# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Finish Replay is a cross-platform (Windows-first, macOS/Linux planned) **Avalonia / .NET 9** desktop
app for sports-timing video: multi-camera recording, frame-accurate replay, latency calibration, and
integration with timing hardware (ALGE TimY3). It is an early MVP — most capture/replay/calibration
backends are deliberately stubbed behind interfaces with `TODO` markers, while the architecture,
models, MVVM wiring and UI are real and compile/run.

## Commands

Run from the repo root (`d:\Development\FinishReplay`).

```bash
dotnet build FinishReplay.sln              # build everything
dotnet run --project src/FinishReplay      # build + launch the app
dotnet format FinishReplay.sln             # apply code style
```

There is **no test project yet**. When one is added, prefer xUnit and run with
`dotnet test` (single test: `dotnet test --filter "FullyQualifiedName~<Name>"`).

In VS Code, F5 uses [.vscode/launch.json](.vscode/launch.json) (preLaunch task `build` from
[.vscode/tasks.json](.vscode/tasks.json)). Recommended extensions: C# Dev Kit + Avalonia
([.vscode/extensions.json](.vscode/extensions.json)).

## Architecture

Single project, layered by folder under [src/FinishReplay/](src/FinishReplay/). UI is **MVVM** using
`CommunityToolkit.Mvvm` source generators (`[ObservableProperty]`, `[RelayCommand]`) — view models
must be `partial`.

- **Composition root**: [MainViewModel](src/FinishReplay/ViewModels/MainViewModel.cs) news up all
  services manually (no DI container yet). Swap for `Microsoft.Extensions.DependencyInjection` here
  without touching views.
- **View resolution**: [ViewLocator](src/FinishReplay/ViewLocator.cs) maps `*.ViewModels.XxxViewModel`
  → `*.Views.XxxView` by name. Adding a page = add a `ViewModelBase`-derived VM + a matching
  `UserControl`; navigation is `MainViewModel.CurrentPage`.

The view models talk only to **interfaces**; concrete backends are placeholders today:

| Concern | Interface | MVP implementation (stub) | Real work lives behind |
|---|---|---|---|
| Camera discovery/open | `ICameraProvider` / `ICameraStream` | `Usb/Mjpeg/RtspCameraProvider` | per-transport capture (FFmpeg/MF/AVF/V4L2) |
| Provider aggregation | `CameraProviderRegistry` / `ICameraManager` | real | — |
| Recording | `IRecordingEngine` + `IVideoBackend` | `RecordingEngine` + `FfmpegVideoBackend` | ffmpeg process + rolling pre/post buffer |
| Replay | `IReplayEngine` | `ReplayEngine` (in-memory clock) | real decoder + multi-stream playback |
| Timeline/markers/offsets | `TimelineEngine` | real | — |
| Timing devices | `ITimingProvider` | `ManualTimingProvider`, `AlgeTimy3TimingProvider` (serial stub) | TimY3 line parser |
| Latency calibration | `ICameraLatencyCalibrationService`, `ITriggerOutput`, `IFlashDetector` | `Fake…Service`, `StubTriggerOutput`, `BrightnessFlashDetector` | LED trigger HW + OpenCV detection |
| Sessions | `ISessionManager` | `SessionManager` (JSON) | — |

### Key cross-cutting concepts (read these together)

- **Provider-based capture**: nothing hardcodes one capture method. A `CameraDevice` carries
  `ProviderName`/`SourceType`/`SourceUrl`; `CameraProviderRegistry` fans discovery out across
  providers and routes `OpenAsync` back to the matching one. Add a transport (e.g. ONVIF) by
  implementing `ICameraProvider` and registering it in `MainViewModel`.
- **Latency calibration → sync offsets**: calibration measures each camera's *absolute* latency
  (`camera_latency = frameArrivalTime − flashTriggerTime`, timed on the monotonic
  [MonotonicClock](src/FinishReplay/Services/Calibration/MonotonicClock.cs)).
  [CameraSyncCalculator](src/FinishReplay/Services/Calibration/CameraSyncCalculator.cs) converts
  absolute latencies into *relative* offsets vs the lowest-latency reference camera — relative offset
  is what synchronized replay uses. `CameraProfile.EffectiveLatencyMs` = calibrated + manual offset.
  Always allow manual correction; prefer measured over guessed offsets.
- **Multi-camera sessions**: a session has many cameras (different protocols/latencies). Each writes
  its own video file and stores `calibratedLatencyMs` / `manualOffsetMs` / `syncOffsetMs`.
  `TimelineEngine.ToCameraTime(cameraId, masterTime)` is the seam for aligning streams during replay.

### Session metadata format

One JSON file per session next to the clips: `<sessionId>.timing.json` (e.g. `race_001.mp4` +
`race_001.timing.json`). Serialized **camelCase**, enums as names, timing offsets in **milliseconds**
(`videoTimeMs`, `syncOffsetMs`). Shape is defined by
[SessionMetadata](src/FinishReplay/Models/SessionMetadata.cs) (`sessionId`, `createdAt`,
`timingProvider`, `cameras[]`, `timingMarkers[]`). Use `SessionMetadata.JsonOptions` for any
(de)serialization so naming/converters stay consistent. Sessions are written to
`<MyVideos>/FinishReplay`.

## Conventions

- Keep the backend boundary clean: UI/VM code must not depend on a concrete capture/replay/timing
  implementation — extend via the interfaces above. Mark unimplemented backend work with `TODO`
  describing the intended approach (this is how the codebase tracks remaining real-integration work).
- `TimeSpan` is the in-memory time type; JSON exposes milliseconds via dedicated properties — don't
  hand-serialize `TimeSpan`.
- Avalonia 11.2 caveats this project already hit: `Grid` has **no** `ColumnSpacing`/`RowSpacing`
  (use child `Margin`); compiled bindings require correct `x:DataType` on every `DataTemplate`;
  a `[ObservableProperty] Foo` generates `OnFooChanged` — don't define a method with that name.

## Deployment

GitHub: `git@github.com:codesource/finishreplay.git`. A repo **deploy key** (write) is used for
publishing; the private key lives at `~/.ssh/finishreplay_deploy` and git is configured to use it via
`core.sshCommand` (`-o IdentitiesOnly=yes`, so it never interferes with other GitHub keys). Publishing
identity: **codesource / admin@code-source.ch**. Just `git push origin main` — auth is automatic.
