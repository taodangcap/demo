# 7zyx Media

Windows x64 WPF cue playback application for live audio, video/projector and karaoke workflows. It uses ManagedBass/BASS, SQLite, WebView2 and MVVM with dependency injection.

## Requirements and build

- Windows x64 and the .NET 10 Desktop Runtime; the WebView2 Runtime is required for video/web output.
- Build: `dotnet restore` then `dotnet build -c Release`.
- Run: `dotnet run --project ShowCuePlayer.csproj -c Debug`.
- Publish: `dotnet publish ShowCuePlayer.csproj -c Release -r win-x64 --self-contained false`.
- Test: `dotnet test -c Release`.

The x64 native file `bass.dll` must remain beside the executable. Supported media is determined by the BASS/WebView2 paths in the application; common imported extensions include those declared by the import service.

## User data and diagnostics

- Logs: `%LocalAppData%\ShowCuePlayer\Logs\`
- Database: `%LocalAppData%\ShowCuePlayer\showcueplayer.db`
- Settings: stored in the SQLite database and embedded in `.showcue` project files where applicable.

When reporting a failure, reproduce it once, close the app, and attach the latest `showcueplayer-YYYYMMDD.log`. Database migration backups are kept beside the database and the original is never overwritten during corruption recovery.
