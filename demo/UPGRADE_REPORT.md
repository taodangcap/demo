# ShowCuePlayer upgrade report

## Architecture

WPF x64 application using Generic Host/DI, MVVM view models, a singleton ManagedBass audio engine, SQLite repositories, WebView2 video/projector windows, and JSON project files. `MainViewModel` remains a large orchestration component; services already isolate audio, metadata, imports, projects, settings, hotkeys, search and video.

## Baseline findings

| Severity | Finding | Main files | Status |
|---|---|---|---|
| Critical | .NET 7 is out of support | `ShowCuePlayer.csproj` | Upgraded to .NET 10 |
| High | Database used roaming AppData and had no schema version/backup | `DatabaseService.cs` | Fixed; legacy DB copied, schema versioned, backups created |
| High | Crash handlers omitted unobserved tasks, exposed stack traces, and swallowed errors | `App.xaml.cs` | Fixed with daily LocalAppData logging and safe UI message |
| High | Audio lifecycle/fades allowed races, invalid volumes and CTS leaks | `BassAudioEngine.cs`, `FadeEngine.cs` | Core lifecycle and fade ownership hardened; hardware stress test remains |
| Medium | Nullable warnings around removed monitors/WebView teardown | video/projector code | Remaining; requires targeted runtime validation |
| Medium | `MainViewModel` has broad responsibilities and several direct dialogs | `MainViewModel.cs` | Existing architecture retained; incremental extraction remains |
| Medium | SQLite native dependency reports NU1903 | transitive `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 | Unresolved upstream dependency; do not suppress warning |

## Repair order and validation limits

Platform/build, diagnostics, persistence, audio lifetime, tests/CI, and documentation were addressed in that order. Physical audio playback, rapid cue stress, WebView2 runtime absence, hot-plugged monitors and projector DPI behavior cannot be truthfully validated in this environment; these require the manual checks in `CHANGELOG_UPGRADE.md`.

ManagedBass remains on the existing stable 3.x API line because changing to 4.x would be a separate audio migration. Only the native `bass.dll` used by the current engine is copied to output.
