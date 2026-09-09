# Build installer

Close 7zyx Media before publishing, then run from the repository root:

```powershell
dotnet publish ShowCuePlayer.csproj -c Release --no-restore -p:PublishProfile=Properties\PublishProfiles\WindowsX64SingleFile.pubxml
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" "installer\ShowCuePlayer.iss"
```

The installer is written to `installer-output\7zyx-Media-Setup-1.0.0-x64.exe`.

## Publish an OTA release on GitHub

The app checks the latest public release of `taodangcap/demo`. Push a semantic-version
tag to run `.github/workflows/release.yml`; GitHub Actions will build, test, package,
hash, and publish the installer automatically:

```powershell
git tag v1.0.1
git push origin main
git push origin v1.0.1
```

Keep the repository or at least its Releases public. The desktop app intentionally
contains no GitHub access token. Release assets must include both:

- `7zyx-Media-Setup-<version>-x64.exe`
- `7zyx-Media-Setup-<version>-x64.exe.sha256`
