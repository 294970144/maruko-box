using System.Net.Http;
using System.Text;
using System.Text.Json;
using MarukoBox.Models;

namespace MarukoBox.Services;

/// <summary>软件自身（MarukoBox）在 GitHub 上的最新 Release 信息。</summary>
public record AppReleaseInfo(string Tag, string Version, string DownloadUrl);

/// <summary>从远端发现的内置 ffmpeg 新版信息。</summary>
public record RemoteFfmpegVersion(string Tag, string DownloadUrl);

/// <summary>内置 ffmpeg 更新推送的判定结果（NVENC API 门槛）。</summary>
public sealed record FfmpegUpdateOffer(bool Offer, string? BlockReason = null);

/// <summary>
/// 软件更新（MarukoBox 自身，仅 GitHub）与内置 ffmpeg 的检查更新。
/// jellyfin-ffmpeg 版本号形如 "7.1.1-5"（tag），资产名
/// jellyfin-ffmpeg_{tag}-portable_win64.zip。
/// </summary>
public interface IUpdateService
{
    /// <summary>当前软件版本（程序集版本，如 "1.2.0"）。</summary>
    string GetAppVersion();

    /// <summary>当前内置 ffmpeg 的版本（读取 ffmpeg\VERSION 标记；未内置返回空字符串）。</summary>
    string GetLocalVersion();

    /// <summary>从 GitHub 查询 jellyfin-ffmpeg 最新 win64 便携版；查询失败抛出异常。</summary>
    Task<RemoteFfmpegVersion> GetLatestFfmpegAsync(CancellationToken ct = default);

    /// <summary>从 GitHub 查询 MarukoBox 自身最新 Release；查询失败抛出异常。</summary>
    Task<AppReleaseInfo> GetLatestAppReleaseAsync(CancellationToken ct = default);

    /// <summary>判定是否应向用户推送某个内置 ffmpeg 新版（NVENC API 门槛）。</summary>
    FfmpegUpdateOffer ShouldOfferFfmpegUpdate(GpuInfo gpu, string targetTag);

    /// <summary>下载并安装新版 ffmpeg 到应用目录的 ffmpeg\ 下（整目录替换，写入 VERSION 标记）。</summary>
    Task DownloadAndInstallAsync(string downloadUrl, string versionTag,
        IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>下载软件安装包到临时目录并返回完整路径（不自动运行，由调用方启动安装器）。</summary>
    Task<string> DownloadAppInstallerAsync(string downloadUrl, string version,
        IProgress<double>? progress = null, CancellationToken ct = default);
}

/// <inheritdoc cref="IUpdateService"/>
public sealed partial class UpdateService : IUpdateService
{
    private static readonly string BundledDir =
        Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    /// <summary>MarukoBox 仓库的 GitHub Releases API。</summary>
    private const string AppRepoLatestApi =
        "https://api.github.com/repos/294970144/maruko-box/releases/latest";

    private readonly HttpClient _http = new()
    {
        // 92MB 解压体 / 34MB 压缩包 / 97MB 安装包在慢速网络下需要足够长的下载窗口
        Timeout = TimeSpan.FromMinutes(10)
    };

    /// <summary>当前软件版本（程序集版本，如 "1.2.0"）；静态版便于非服务上下文调用。</summary>
    public static string GetAppVersionStatic() =>
        typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <inheritdoc/>
    public string GetAppVersion() => GetAppVersionStatic();

    /// <inheritdoc/>
    public string GetLocalVersion() => ConfigService.GetBundledVersion();

    /// <inheritdoc/>
    public async Task<RemoteFfmpegVersion> GetLatestFfmpegAsync(CancellationToken ct = default)
    {
        // jellyfin-ffmpeg 的 8.x 系列在 GitHub 上标为 prerelease（7.x 已停止更新，
        // 稳定版止于 7.1.4-3）。/releases/latest 只返回稳定版，会漏掉 8.x——
        // 而 8.x 恰恰是需要 NVENC API 13.1 门槛、且被此前镜像渠道选中的版本。
        // 因此这里拉取全部 release（跳过 draft），按版本号选最高者，保证与
        // 「内置 ffmpeg 更新」语义一致：始终能发现 8.x 并正确应用 NVENC 门槛。
        using var doc = await GetReleaseJsonAsync(
            "https://api.github.com/repos/jellyfin/jellyfin-ffmpeg/releases?per_page=30", ct)
            .ConfigureAwait(false);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("GitHub 返回的 ffmpeg Release 列表格式异常");
        }

        RemoteFfmpegVersion? best = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                continue; // 跳过草稿
            }

            var rawTag = release.TryGetProperty("tag_name", out var tn) ? tn.GetString() : null;
            var tag = NormalizeTag(rawTag ?? string.Empty);
            var url = FindPortableZipUrl(release);

            if (string.IsNullOrEmpty(tag) || url is null)
            {
                continue;
            }

            // CompareVersions(a, b) > 0 表示 a 较新；此处 tag 较 best 新则替换。
            // 注意：返回值语义是「b 较新返回负数」，切不可写成 < 0，否则会选到最旧版本。
            if (best is null || CompareVersions(tag, best.Tag) > 0)
            {
                best = new RemoteFfmpegVersion(tag, url);
            }
        }

        if (best is null)
        {
            throw new InvalidOperationException("GitHub 上未找到可用的 win64 便携版 ffmpeg 资产");
        }

        return best;
    }

    /// <summary>
    /// 在单个 release 的资产列表中查找 win64 便携版 zip 的下载地址。
    /// 资产名随版本演进（旧: -portable_win64.zip；新: _portable_win64-clang-gpl.zip），
    /// 统一按「含 portable_win64 且以 .zip 结尾」匹配；portable_winarm64 不含该子串，不会误命中。
    /// </summary>
    private static string? FindPortableZipUrl(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is not null
                && name.Contains("portable_win64", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<AppReleaseInfo> GetLatestAppReleaseAsync(CancellationToken ct = default)
    {
        using var doc = await GetReleaseJsonAsync(AppRepoLatestApi, ct).ConfigureAwait(false);

        // tag 形如 "v1.2.0" → 版本 "1.2.0"
        var rawTag = doc.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
        var version = NormalizeTag(rawTag);

        string? url = null;
        if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                // 安装包资产名：MarukoBoxSetup-Inno_1.2.0.exe
                if (name is not null
                    && name.StartsWith("MarukoBoxSetup", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(version))
        {
            throw new InvalidOperationException("GitHub 上未找到 MarukoBox 的最新 Release");
        }
        if (string.IsNullOrEmpty(url))
        {
            throw new InvalidOperationException($"Release {rawTag} 未附带安装包资产");
        }

        return new AppReleaseInfo(rawTag, version, url);
    }

    /// <inheritdoc/>
    public FfmpegUpdateOffer ShouldOfferFfmpegUpdate(GpuInfo gpu, string targetTag) =>
        ShouldOfferFfmpegUpdateStatic(gpu, targetTag);

    /// <summary>
    /// 判定是否应向用户推送内置 ffmpeg 更新。
    /// jellyfin-ffmpeg 8.x 起的 NVENC 基于 Video Codec SDK 13.1 编译，要求
    /// NVIDIA Windows 驱动 ≥610（即 NVENC API ≥13.1，NVIDIA 官方系统要求）；
    /// 驱动过旧时升级会导致 NVENC 硬件编码不可用，此时不推送该更新。
    /// 7.x 无此门槛。AMD（AMF SDK 官方声明向后兼容所有历史驱动版本）与
    /// Intel QSV 无同类硬门槛，不做限制。
    /// </summary>
    public static FfmpegUpdateOffer ShouldOfferFfmpegUpdateStatic(GpuInfo gpu, string targetTag)
    {
        var requiredDriver = ParseTagMajor(targetTag) >= 8 ? 610 : (int?)null;
        if (requiredDriver is null)
        {
            // 目标版本无 NVENC 门槛
            return new FfmpegUpdateOffer(true);
        }

        // 未检测到 NVIDIA 驱动（无 N 卡或 nvidia-smi 不可用）：无从判定，按可推送处理
        if (string.IsNullOrEmpty(gpu.DriverVersion) || gpu.DriverVersion == "未知")
        {
            return new FfmpegUpdateOffer(true);
        }

        if (!int.TryParse(gpu.DriverVersion.Split('.')[0], out var driverMajor))
        {
            return new FfmpegUpdateOffer(true);
        }

        return driverMajor >= requiredDriver
            ? new FfmpegUpdateOffer(true)
            : new FfmpegUpdateOffer(false,
                $"本机 NVIDIA 驱动 {gpu.DriverVersion}（NVENC API {gpu.NvencApiVersion}）低于 "
                + $"ffmpeg {ParseTagMajor(targetTag)}.x 所需的 API 13.1（需驱动 ≥610），"
                + "升级后 NVENC 硬件编码将不可用");
    }

    /// <summary>"v7.1.4-3" -> "7.1.4-3"（仅在 v/V 后紧跟数字时剥前缀）。</summary>
    public static string NormalizeTag(string tag)
    {
        if (tag.Length > 1 && (tag[0] == 'v' || tag[0] == 'V') && char.IsDigit(tag[1]))
        {
            return tag[1..];
        }
        return tag;
    }

    /// <summary>取版本 tag 的主版本号："8.1.2-3" → 8；解析失败返回 0。</summary>
    private static int ParseTagMajor(string tag)
    {
        var t = NormalizeTag(tag ?? string.Empty);
        var dot = t.IndexOf('.');
        var head = dot < 0 ? t : t[..dot];
        return int.TryParse(head, out var major) ? major : 0;
    }

    /// <summary>请求 GitHub Releases API 并解析 JSON（带 User-Agent，GitHub API 强制要求）。</summary>
    private async Task<JsonDocument> GetReleaseJsonAsync(string apiUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.UserAgent.ParseAdd($"MarukoBox/{GetAppVersionStatic()} (+https://github.com/294970144/maruko-box)");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonDocument.Parse(json);
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
            await DownloadFileAsync(downloadUrl, tempZip, progress, ct).ConfigureAwait(false);

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

    /// <inheritdoc/>
    public async Task<string> DownloadAppInstallerAsync(string downloadUrl, string version,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dest = Path.Combine(Path.GetTempPath(), $"MarukoBoxSetup_{version}.exe");
        await DownloadFileAsync(downloadUrl, dest, progress, ct).ConfigureAwait(false);
        return dest;
    }

    /// <summary>流式下载文件到目标路径（带进度与取消支持）。</summary>
    private async Task DownloadFileAsync(string url, string destPath,
        IProgress<double>? progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd($"MarukoBox/{GetAppVersionStatic()}");
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        await using var remote = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var local = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None,
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
