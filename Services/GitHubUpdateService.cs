using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShowCuePlayer.Services;

public sealed record AppUpdateInfo(
    Version Version,
    string TagName,
    string ReleaseName,
    string Notes,
    string InstallerName,
    Uri DownloadUri,
    long Size,
    string? Sha256Digest,
    Uri ReleasePage);

public sealed record UpdateCheckResult(AppUpdateInfo? Update, string? Error)
{
    public bool IsUpdateAvailable => Update is not null;
}

public interface IGitHubUpdateService
{
    Version CurrentVersion { get; }
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default);
    Task<string> DownloadInstallerAsync(
        AppUpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
    void LaunchInstaller(string installerPath);
}

/// <summary>Safe, operator-approved OTA updater backed by public GitHub Releases.</summary>
public sealed class GitHubUpdateService : IGitHubUpdateService, IDisposable
{
    public const string RepositoryOwner = "taodangcap";
    public const string RepositoryName = "demo";
    private const string InstallerPrefix = "7zyx-Media-Setup-";
    private readonly HttpClient _httpClient;

    public Version CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0);

    public GitHubUpdateService()
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("7zyx-Media-Updater/1.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = new Uri(
                $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest");
            using var response = await _httpClient.GetAsync(endpoint,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new UpdateCheckResult(null,
                    "GitHub chưa có Release công khai cho 7zyx Media.");
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream,
                cancellationToken: cancellationToken);
            if (release is null || release.Draft || release.Prerelease)
                return new UpdateCheckResult(null, "GitHub Release không hợp lệ.");

            if (!TryParseReleaseVersion(release.TagName, out var releaseVersion))
                return new UpdateCheckResult(null,
                    $"Tag Release '{release.TagName}' không đúng dạng v1.2.3.");
            if (releaseVersion <= CurrentVersion)
                return new UpdateCheckResult(null, null);

            var asset = release.Assets.FirstOrDefault(candidate =>
                candidate.Name.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase)
                && candidate.Name.EndsWith("-x64.exe", StringComparison.OrdinalIgnoreCase));
            if (asset is null || !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var downloadUri)
                              || downloadUri.Scheme != Uri.UriSchemeHttps
                              || !string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                return new UpdateCheckResult(null,
                    "Release mới chưa có installer 7zyx Media x64 hợp lệ.");

            var digest = asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
                ? asset.Digest[7..]
                : null;
            if (!IsSha256(digest))
            {
                var checksumAsset = release.Assets.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, asset.Name + ".sha256",
                        StringComparison.OrdinalIgnoreCase));
                if (checksumAsset is not null
                    && Uri.TryCreate(checksumAsset.BrowserDownloadUrl, UriKind.Absolute, out var checksumUri)
                    && checksumUri.Scheme == Uri.UriSchemeHttps
                    && string.Equals(checksumUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                {
                    var checksumText = await _httpClient.GetStringAsync(checksumUri, cancellationToken);
                    digest = checksumText.Split((char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                }
            }
            if (!IsSha256(digest))
                return new UpdateCheckResult(null,
                    "Release mới chưa có mã SHA-256 để xác minh an toàn.");

            var releasePage = Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var page)
                ? page
                : new Uri($"https://github.com/{RepositoryOwner}/{RepositoryName}/releases");
            return new UpdateCheckResult(new AppUpdateInfo(
                releaseVersion,
                release.TagName,
                string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
                release.Body ?? string.Empty,
                asset.Name,
                downloadUri,
                asset.Size,
                digest,
                releasePage), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(null, $"Không kiểm tra được cập nhật: {ex.Message}");
        }
    }

    public async Task<string> DownloadInstallerAsync(
        AppUpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (update.DownloadUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(update.DownloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Địa chỉ tải cập nhật không thuộc GitHub.");

        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "7zyx Media", "Updates", update.TagName);
        Directory.CreateDirectory(updateDirectory);
        var safeName = Path.GetFileName(update.InstallerName);
        var finalPath = Path.Combine(updateDirectory, safeName);
        var partialPath = finalPath + ".partial";
        File.Delete(partialPath);

        using var response = await _httpClient.GetAsync(update.DownloadUri,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var expectedSize = update.Size > 0
            ? update.Size
            : response.Content.Headers.ContentLength.GetValueOrDefault();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(partialPath, FileMode.Create, FileAccess.Write,
            FileShare.None, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 128];
        long downloaded = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            sha256.AppendData(buffer, 0, read);
            downloaded += read;
            if (expectedSize > 0)
                progress?.Report(Math.Clamp(downloaded / (double)expectedSize, 0, 1));
        }
        await output.FlushAsync(cancellationToken);

        if (expectedSize > 0 && downloaded != expectedSize)
        {
            File.Delete(partialPath);
            throw new InvalidDataException(
                $"File cập nhật tải thiếu dữ liệu ({downloaded}/{expectedSize} byte).");
        }

        var actualDigest = Convert.ToHexString(sha256.GetHashAndReset());
        if (!string.IsNullOrWhiteSpace(update.Sha256Digest)
            && !string.Equals(actualDigest, update.Sha256Digest,
                StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partialPath);
            throw new InvalidDataException("SHA-256 của bộ cài không khớp GitHub Release.");
        }

        File.Move(partialPath, finalPath, overwrite: true);
        progress?.Report(1);
        return finalPath;
    }

    public void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath)
            || !string.Equals(Path.GetExtension(installerPath), ".exe",
                StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Không tìm thấy bộ cài cập nhật.", installerPath);

        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/SP- /SILENT /CLOSEAPPLICATIONS /NORESTART",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? string.Empty
        });
    }

    public static bool TryParseReleaseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        var value = tag?.Trim().TrimStart('v', 'V').Split('-', 2)[0];
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('.');
        if (parts.Length is < 1 or > 4 || parts.Any(part => !int.TryParse(part, out _)))
            return false;
        value = parts.Length switch
        {
            1 => value + ".0.0",
            2 => value + ".0",
            _ => value
        };
        return Version.TryParse(value, out version!);
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public void Dispose() => _httpClient.Dispose();

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = string.Empty;
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = string.Empty;
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }
}
