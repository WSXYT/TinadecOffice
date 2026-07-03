using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace TinadecTools.Tools.Search;

// ── GitHub API 最小模型 ───────────────────────────────────────────────────────

internal sealed class GhRelease
{
    [JsonPropertyName("tag_name")] public string TagName  { get; set; } = string.Empty;
    [JsonPropertyName("assets")]   public List<GhAsset> Assets { get; set; } = new();
}

internal sealed class GhAsset
{
    [JsonPropertyName("name")]                 public string Name               { get; set; } = string.Empty;
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(GhRelease))]
[JsonSerializable(typeof(GhAsset))]
internal partial class GhJsonContext : JsonSerializerContext;

// ── 下载器 ────────────────────────────────────────────────────────────────────

internal static class RipgrepDownloader
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/BurntSushi/ripgrep/releases/latest";

    // ponytail: test escape hatch, no interface/mock needed
    internal static bool SkipAutoDownload;

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders =
        {
            UserAgent = { new ProductInfoHeaderValue("TinadecTools", "1.0") },
            Accept    = { new MediaTypeWithQualityHeaderValue("application/vnd.github+json") }
        },
        Timeout = TimeSpan.FromSeconds(60)
    };

    public static async Task<(bool Success, string Detail)> TryDownloadAsync(
        string targetPath,
        CancellationToken cancellationToken)
    {
        if (SkipAutoDownload)
            return (false, "auto-download disabled.");

        Logger.Info("ripgrep 未找到，尝试从 GitHub 自动下载...");

        try
        {
            using var releaseResp = await Http.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
            releaseResp.EnsureSuccessStatusCode();

            await using var releaseStream = await releaseResp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync(
                releaseStream, GhJsonContext.Default.GhRelease, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("GitHub API 响应解析失败。");

            var suffix = GetAssetSuffix();
            var asset  = release.Assets.FirstOrDefault(a => a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Release {release.TagName} 中找不到适用于当前平台的 asset（期望后缀：{suffix}）。");

            Logger.Info("找到 asset：{name}，开始下载...", asset.Name);

            var tempFile = Path.Combine(Path.GetTempPath(), $"rg-download-{Guid.NewGuid():N}{Path.GetExtension(asset.Name)}");
            try
            {
                using var dlResp = await Http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                dlResp.EnsureSuccessStatusCode();

                await using var src  = await dlResp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var dest = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024);
                await src.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    ExtractFromZip(tempFile, targetPath);
                else
                    await ExtractFromTarGzAsync(tempFile, targetPath, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { } // ponytail: inline, called once
            }

            SetExecutableIfUnix(targetPath);
            Logger.Info("ripgrep {version} 已下载到 {path}", release.TagName, targetPath);
            return (true, release.TagName);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.Warn(ex, "自动下载 ripgrep 失败");
            return (false, ex.Message);
        }
    }

    // ── 私有辅助 ──────────────────────────────────────────────────────────────

    private static void ExtractFromZip(string zipPath, string targetPath)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.Name.Equals("rg.exe", StringComparison.OrdinalIgnoreCase) ||
            e.Name.Equals("rg",     StringComparison.Ordinal))
            ?? throw new InvalidOperationException("ZIP 包中未找到 rg 或 rg.exe。");
        entry.ExtractToFile(targetPath, overwrite: true);
    }

    private static async Task ExtractFromTarGzAsync(string tarGzPath, string targetPath, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rg-extract-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await using var fs   = new FileStream(tarGzPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024);
            await using var gzip = new GZipStream(fs, CompressionMode.Decompress);
            await System.Formats.Tar.TarFile.ExtractToDirectoryAsync(gzip, tempDir, true, ct).ConfigureAwait(false);

            var rg = Directory.GetFiles(tempDir, "rg", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidOperationException("TAR.GZ 包中未找到 rg 二进制。");
            File.Copy(rg, targetPath, overwrite: true);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { } // ponytail: inline, called once
        }
    }

    private static string GetAssetSuffix()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64   => "x86_64",
            Architecture.Arm64 => "aarch64",
            _                  => "x86_64"
        };
        if (OperatingSystem.IsWindows()) return $"-{arch}-pc-windows-msvc.zip";
        if (OperatingSystem.IsMacOS())   return $"-{arch}-apple-darwin.tar.gz";
        return $"-{arch}-unknown-linux-musl.tar.gz";
    }

    private static void SetExecutableIfUnix(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        var mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }
}
