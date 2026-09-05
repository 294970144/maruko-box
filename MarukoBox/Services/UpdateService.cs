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

        // tag 形如 "v7.1.4-3"（带 v 前缀），规范化去掉以便与 VERSION 标记比较
        var rawTag = doc.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
        var tag = NormalizeTag(rawTag);

        string? url = null;
        if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                // win64 便携版 zip。资产名随版本演进：
                //   旧: jellyfin-ffmpeg_7.1.1-5-portable_win64.zip
                //   新: jellyfin-ffmpeg_7.1.4-3_portable_win64-clang-gpl.zip
                if (name is not null
                    && name.Contains("portable_win64", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
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

    /// <summary>"v7.1.4-3" -> "7.1.4-3"（仅在 v/V 后紧跟数字时剥前缀）。</summary>
    private static string NormalizeTag(string tag)
    {
        if (tag.Length > 1 && (tag[0] == 'v' || tag[0] == 'V') && char.IsDigit(tag[1]))
        {
            return tag[1..];
        }
        return tag;
    }

    // ---------- 国内镜像（兰州大学开源镜像站，Apache autoindex 目录） ----------

    private const string MirrorBase = "https://mirror.lzu.edu.cn/jellyfin/ffmpeg/windows/";

    // 目录为四层结构（实测 2026-09）：
    //   windows/{大版本}.x/{tag}/win64/jellyfin-ffmpeg_{tag}[-_]portable_win64*.zip
    //   例: windows/7.x/7.1.1-5/win64/jellyfin-ffmpeg_7.1.1-5-portable_win64.zip
    private static readonly Regex MirrorMajorsRegex =
        new(@"href=""(\d+)\.x/""", RegexOptions.Compiled);

    private static readonly Regex MirrorTagsRegex =
        new(@"href=""(\d+(?:\.\d+)+-\d+)/""", RegexOptions.Compiled);

    private async Task<RemoteFfmpegVersion> GetLatestFromMirrorAsync(CancellationToken ct)
    {
        // 1) 列大版本子目录（5.x/6.x/7.x/8.x...），取数字最大者——最高完整版本必然在其中
        var rootHtml = await HttpGetStringAsync(MirrorBase, ct).ConfigureAwait(false);
        var majors = MirrorMajorsRegex.Matches(rootHtml)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .ToList();
        if (majors.Count == 0)
        {
            throw new InvalidOperationException("镜像站目录结构已变化，未找到版本子目录");
        }
        var major = majors.Max();

        // 2) 列该大版本下的全部 tag（如 8.1.2-3），取版本最高者
        var majorHtml = await HttpGetStringAsync($"{MirrorBase}{major}.x/", ct).ConfigureAwait(false);
        var tags = MirrorTagsRegex.Matches(majorHtml)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
        if (tags.Count == 0)
        {
            throw new InvalidOperationException($"镜像站 {major}.x/ 下未找到任何版本目录");
        }
        var bestTag = tags[0];
        foreach (var t in tags)
        {
            // CompareVersions: a 较新返回正数——t 更新时替换 bestTag
            if (CompareVersions(t, bestTag) > 0)
            {
                bestTag = t;
            }
        }

        // 3) 该 tag 的 win64/ 下找 zip。命名两代并存：
        //    旧: jellyfin-ffmpeg_{tag}-portable_win64.zip          （优先，体积小）
        //    新: jellyfin-ffmpeg_{tag}_portable_win64-clang-gpl.zip
        var win64Html = await HttpGetStringAsync($"{MirrorBase}{major}.x/{bestTag}/win64/", ct).ConfigureAwait(false);
        var escapedTag = Regex.Escape(bestTag);
        var zipMatch = Regex.Match(win64Html, $@"href=""(jellyfin-ffmpeg_{escapedTag}-portable_win64\.zip)""");
        if (!zipMatch.Success)
        {
            zipMatch = Regex.Match(win64Html, $@"href=""(jellyfin-ffmpeg_{escapedTag}_portable_win64[^""]*\.zip)""");
        }
        if (!zipMatch.Success)
        {
            throw new InvalidOperationException($"镜像站 {bestTag} 下未找到 win64 便携版压缩包");
        }

        return new RemoteFfmpegVersion(bestTag, $"{MirrorBase}{major}.x/{bestTag}/win64/{zipMatch.Groups[1].Value}");
    }

    private async Task<string> HttpGetStringAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("MarukoBox/1.0");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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

        // 解压目录必须与应用目录同卷：Directory.Move 只支持同卷原子 rename，
        // 跨卷（如 Temp 在 C:、应用装在 D:/E:）会抛 IOException，导致替换失败甚至旧版丢失。
        var tempExtract = Path.Combine(AppContext.BaseDirectory, $".ffmpeg_extract_{Guid.NewGuid():N}");

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

            // 4) 整目录替换（中断安全）：先把旧目录改名让位（同卷 rename，近乎原子），
            //    再移入新目录，最后删备份。任何一步被中断（进程崩溃/断电），
            //    ffmpeg\ 始终保留一个可用版本；失败原因通常是文件被占用（编码中）。
            try
            {
                if (Directory.Exists(BundledDir))
                {
                    var backup = BundledDir + ".old";
                    if (Directory.Exists(backup))
                    {
                        Directory.Delete(backup, recursive: true);
                    }
                    Directory.Move(BundledDir, backup);
                    Directory.Move(tempExtract, BundledDir);
                    Directory.Delete(backup, recursive: true);
                }
                else
                {
                    Directory.Move(tempExtract, BundledDir);
                }
            }
            catch (Exception ex)
            {
                throw new IOException("无法替换内置 ffmpeg：文件可能正被占用，请等待当前任务完成或关闭应用后重试。", ex);
            }

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
