using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MarukoBox.Services;

/// <summary>内置 ffmpeg 的更新渠道。</summary>
public enum UpdateChannel
{
    /// <summary>国内镜像站（兰州大学开源镜像站）。</summary>
    Mirror,

    /// <summary>GitHub Releases。</summary>
    GitHub
}

/// <summary>从远端渠道发现的 ffmpeg 版本信息。</summary>
public record RemoteFfmpegVersion(string Tag, string DownloadUrl);

/// <summary>
/// 内置 ffmpeg 的检查与更新。
/// jellyfin-ffmpeg 版本号形如 "7.1.1-5"（tag），资产名
/// jellyfin-ffmpeg_{tag}-portable_win64.zip。
/// </summary>
public interface IUpdateService
{
    /// <summary>当前内置 ffmpeg 的版本（读取 ffmpeg\VERSION 标记；未内置返回空字符串）。</summary>
    string GetLocalVersion();

    /// <summary>从指定渠道查询最新版本；查询失败抛出异常。</summary>
    Task<RemoteFfmpegVersion> GetLatestVersionAsync(UpdateChannel channel, CancellationToken ct = default);

    /// <summary>下载并安装新版 ffmpeg 到应用目录的 ffmpeg\ 下（整目录替换，写入 VERSION 标记）。</summary>
    Task DownloadAndInstallAsync(string downloadUrl, string versionTag,
        IProgress<double>? progress = null, CancellationToken ct = default);
}

/// <inheritdoc cref="IUpdateService"/>
public sealed partial class UpdateService : IUpdateService
{
    private static readonly string BundledDir =
        Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    private readonly HttpClient _http = new()
    {
        // 92MB 解压体 / 34MB 压缩包在慢速网络下需要足够长的下载窗口
        Timeout = TimeSpan.FromMinutes(10)
    };

    /// <summary>渠道的展示名（与设置页下拉框一致）。</summary>
    public static string ChannelDisplayName(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Mirror => "国内镜像站",
        UpdateChannel.GitHub => "GitHub",
        _ => channel.ToString()
    };

    /// <summary>解析渠道存储值（config.json 中）为枚举。</summary>
    public static UpdateChannel ParseChannel(string? code) =>
        string.Equals(code, "github", StringComparison.OrdinalIgnoreCase)
            ? UpdateChannel.GitHub
            : UpdateChannel.Mirror;

    /// <inheritdoc/>
    public string GetLocalVersion() => ConfigService.GetBundledVersion();

    /// <inheritdoc/>
    public async Task<RemoteFfmpegVersion> GetLatestVersionAsync(UpdateChannel channel, CancellationToken ct = default)
    {
        return channel switch
        {
            UpdateChannel.GitHub => await GetLatestFromGitHubAsync(ct).ConfigureAwait(false),
            _ => await GetLatestFromMirrorAsync(ct).ConfigureAwait(false)
        };
    }

    // ---------- GitHub ----------

    private async Task<RemoteFfmpegVersion> GetLatestFromGitHubAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/jellyfin/jellyfin-ffmpeg/releases/latest");
        // GitHub API 强制要求 User-Agent，否则 403
        request.Headers.UserAgent.ParseAdd("MarukoBox/1.0 (+https://github.com/)");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;

        string? url = null;
        if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is not null && name.Contains("portable_win64.zip", StringComparison.OrdinalIgnoreCase))
                {
                    url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(url))
        {
            throw new InvalidOperationException("GitHub 上未找到可用的 win64 便携版 ffmpeg 资产");
        }

        return new RemoteFfmpegVersion(tag, url);
    }

    // ---------- 国内镜像（兰州大学开源镜像站，HTML 目录索引） ----------

    [GeneratedRegex(@"jellyfin-ffmpeg_(\d+(?:\.\d+){1,2})-(\d+)-portable_win64\.zip",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MirrorFileRegex();

    private async Task<RemoteFfmpegVersion> GetLatestFromMirrorAsync(CancellationToken ct)
    {
        const string indexUrl = "https://mirror.lzu.edu.cn/jellyfin/ffmpeg/windows/";

        using var request = new HttpRequestMessage(HttpMethod.Get, indexUrl);
        request.Headers.UserAgent.ParseAdd("MarukoBox/1.0");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        // 目录索引里逐条匹配 jellyfin-ffmpeg_7.1.1-5-portable_win64.zip，取版本最高者
        RemoteFfmpegVersion? best = null;
        foreach (Match m in MirrorFileRegex().Matches(html))
        {
            var tag = $"{m.Groups[1].Value}-{m.Groups[2].Value}";
            var url = indexUrl + m.Value;
            if (best is null || CompareVersions(tag, best.Tag) > 0)
            {
                best = new RemoteFfmpegVersion(tag, url);
            }
        }

        return best
            ?? throw new InvalidOperationException("镜像站上未找到可用的 ffmpeg 版本（目录页可能暂时不可达）");
    }

    // ---------- 版本比较 ----------

    /// <summary>比较形如 "7.1.1-5" 的版本号；b 较新返回负数，相同返回 0，a 较新返回正数。</summary>
    public static int CompareVersions(string a, string b)
    {
        var va = ParseVersion(a);
        var vb = ParseVersion(b);
        for (var i = 0; i < Math.Max(va.Length, vb.Length); i++)
        {
            var x = i < va.Length ? va[i] : 0;
            var y = i < vb.Length ? vb[i] : 0;
            if (x != y)
            {
                return y - x > 0 ? -1 : 1;
            }
        }
        return 0;
    }

    private static int[] ParseVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return Array.Empty<int>();
        }

        // "7.1.1-5" -> 7,1,1,5；容忍任意数量数字段
        var parts = version.TrimStart('v', 'V').Split('-', '.');
        var nums = new List<int>();
        foreach (var p in parts)
        {
            if (int.TryParse(p, out var n))
            {
                nums.Add(n);
            }
        }
        return nums.ToArray();
    }

    // ---------- 下载与安装 ----------

    /// <inheritdoc/>
    public async Task DownloadAndInstallAsync(string downloadUrl, string versionTag,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"MarukoBox_ffmpeg_{Guid.NewGuid():N}.zip");
        var tempExtract = Path.Combine(Path.GetTempPath(), $"MarukoBox_ffmpeg_{Guid.NewGuid():N}");

        try
        {
            // 1) 流式下载（带进度与取消支持）
            using (var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl))
            {
                request.Headers.UserAgent.ParseAdd("MarukoBox/1.0");
                using var response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? -1;
                await using var remote = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var local = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 1 << 16, useAsync: true);

                var buffer = new byte[1 << 16];
                long written = 0;
                int read;
                while ((read = await remote.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await local.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    written += read;
                    if (total > 0)
                    {
                        progress?.Report(written * 100.0 / total);
                    }
                }
            }

            // 2) 解压到临时目录
            Directory.CreateDirectory(tempExtract);
            System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, tempExtract);

            // 3) 校验解压结果必须包含 ffmpeg.exe
            var newFfmpeg = Path.Combine(tempExtract, "ffmpeg.exe");
            if (!File.Exists(newFfmpeg))
            {
                throw new InvalidOperationException("下载的压缩包中没有 ffmpeg.exe，已放弃安装");
            }

            // 4) 整目录替换：先删旧的 ffmpeg\，再移入新的
            //    ffmpeg.exe 被占用（如正在编码）时这里会失败，给出可操作的提示。
            try
            {
                if (Directory.Exists(BundledDir))
                {
                    Directory.Delete(BundledDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                throw new IOException("无法替换内置 ffmpeg：文件可能正被占用，请等待当前任务完成或关闭应用后重试。", ex);
            }

            Directory.Move(tempExtract, BundledDir);

            // 5) 写入版本标记
            await File.WriteAllTextAsync(
                Path.Combine(BundledDir, "VERSION"),
                versionTag,
                new UTF8Encoding(false), ct).ConfigureAwait(false);

            progress?.Report(100);
        }
        finally
        {
            TryCleanup(tempZip);
            TryCleanup(tempExtract);
        }
    }

    private static void TryCleanup(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 临时文件清理失败不影响主流程
        }
    }
}
