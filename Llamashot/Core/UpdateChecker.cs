using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Llamashot.Core;

public static class UpdateChecker
{
    private const string GitHubApiUrl = "https://api.github.com/repos/santos-k/Llamashot/releases/latest";
    private static readonly HttpClient Http = new();

    static UpdateChecker()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Llamashot-UpdateChecker");
        Http.Timeout = TimeSpan.FromSeconds(15);
    }

    public record UpdateInfo(string Version, string DownloadUrl, string ReleaseNotes);

    /// <summary>Cached result from the most recent update check (null = no update or not checked).</summary>
    public static UpdateInfo? LatestUpdate { get; private set; }

    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        var response = await Http.GetAsync(GitHubApiUrl);
        response.EnsureSuccessStatusCode();

        var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();
        if (release == null) { LatestUpdate = null; return null; }

        var latestVersion = release.TagName.TrimStart('v');
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        if (!IsNewer(latestVersion, currentVersion))
        { LatestUpdate = null; return null; }

        // Find the installer asset (setup exe)
        var installerAsset = release.Assets?.FirstOrDefault(a =>
            a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        // Fall back to any exe asset
        installerAsset ??= release.Assets?.FirstOrDefault(a =>
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        var downloadUrl = installerAsset?.BrowserDownloadUrl ?? release.HtmlUrl;

        LatestUpdate = new UpdateInfo(latestVersion, downloadUrl, release.Body ?? "");
        return LatestUpdate;
    }

    public static async Task<string?> DownloadUpdateAsync(UpdateInfo update, IProgress<double>? progress = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Llamashot_Update");
        Directory.CreateDirectory(tempDir);

        var fileName = $"LlamashotSetup_v{update.Version}.exe";
        var filePath = Path.Combine(tempDir, fileName);

        using var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using var fileStream = File.Create(filePath);

        if (progress == null || totalBytes <= 0)
        {
            await response.Content.CopyToAsync(fileStream);
        }
        else
        {
            var buffer = new byte[81920];
            long downloaded = 0;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloaded += bytesRead;
                progress.Report((double)downloaded / totalBytes * 100);
            }
        }

        return filePath;
    }

    private static bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(latest, out var l) && Version.TryParse(current, out var c))
            return l > c;
        return false;
    }

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
