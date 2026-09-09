# Upgrade changelog

## Changed

- Targeted .NET 10 Windows x64 and upgraded Microsoft.Extensions, SQLite, WebView2 and CommunityToolkit packages.
- Added daily file logging, three global exception paths, safer shutdown logging and user-friendly fatal error text.
- Moved new database storage to LocalAppData, preserved the roaming legacy database, added schema versioning and pre-migration/corruption backups.
- Added settings validation, corrupt JSON recovery, finite volume/fade checks and hotkey normalization.
- Serialized core BASS lifecycle operations, improved stream-free diagnostics, and made fade cancellation thread-safe/disposable.
- Added MSTest coverage, a solution file and Windows GitHub Actions build.

## Remaining risks

- Audio/device hot-plug and rapid Play/Stop need testing with the deployed BASS x64 binaries and real devices.
- Projector/WebView windows need multi-monitor, mixed-DPI, disconnect and shutdown testing.
- Existing nullable warnings in video/projector paths require deeper behavioral work.
- The transitive SQLite native package currently triggers NU1903; track the Microsoft.Data.Sqlite dependency update.

## Manual test checklist

Launch the app; import audio; Play/Pause/Resume/Stop; switch cues rapidly; switch audio output; open/close projector on each monitor; disconnect the output monitor; close while playing; restart and verify project/settings; inspect `%LocalAppData%\ShowCuePlayer\Logs` for unhandled exceptions and verify all three BASS DLLs beside the Release executable.
