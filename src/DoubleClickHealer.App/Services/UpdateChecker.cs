using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace DoubleClickHealer.App.Services;

internal static class UpdateChecker
{
    public const string ReleasesUrl = "https://github.com/yaohongcool/DoubleClickHealer/releases";

    private const string LatestReleaseApi =
        "https://api.github.com/repos/yaohongcool/DoubleClickHealer/releases/latest";

    /// <summary>
    /// 返回 GitHub 最新 release 的版本号；仓库还没有 release 时返回 null。
    /// 网络或解析错误会抛出异常，由调用方提示失败。
    /// </summary>
    public static async Task<Version?> GetLatestVersionAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DoubleClickHealer");

        using HttpResponseMessage response = await client.GetAsync(LatestReleaseApi).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("tag_name", out JsonElement tag))
            return null;

        string? tagName = tag.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(tagName))
            return null;

        tagName = tagName.TrimStart('v', 'V');
        return Version.TryParse(tagName, out Version? version) ? version : null;
    }

    public static bool IsNewer(Version latest, Version current) =>
        Normalize(latest) > Normalize(current);

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
