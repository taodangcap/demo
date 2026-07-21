# Build installer

Close ShowCuePlayer before publishing, then run from the repository root:

```powershell
dotnet publish ShowCuePlayer.csproj -c Release --no-restore -p:PublishProfile=Properties\PublishProfiles\WindowsX64SingleFile.pubxml
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" "installer\ShowCuePlayer.iss"
```

The installer is written to `installer-output\ShowCuePlayer-Setup-1.0.0-x64.exe`.
