using System.Net.Http;
using System.Text;
using System.Text.Json;
using MarukoBox.Models;

namespace MarukoBox.Services;

/// <summary>软件自身（MarukoBox）在 GitHub 上的最新 Release 信息。</summary>
public record AppReleaseInfo(string Tag, string Version, string DownloadUrl);

/// <summary>软件自身更新源（MarukoBox 仓库镜像）。GitHub 为主源，Gitee 为国内镜像。</summary>
public enum UpdateSource
{
    /// <summary>GitHub Releases（默认，海外，速度视网络）。</summary>
    GitHub,

    /// <summary>Gitee 镜像（国内访问更快；匿名只读公开仓库）。</summary>
    Gitee
}

/// <summary>
/// 从远端 jellyfin-ffmpeg release 拉到的完整信息（含资产、时间、是否 prerelease）。
/// 用于「程序员」专列版本列表展示与按驱动兼容性过滤。
/// </summary>
public sealed record RemoteFfmpegRelease(
    string Tag,
    string AssetUrl,
    DateTimeOffset PublishedAt,
    long AssetSizeBytes,
    bool IsPrerelease);

/// <summary>
/// 按本机驱动兼容性筛选取的 ffmpeg 推荐升级目标。
/// 「驱动门槛」按 jellyfin-ffmpeg 8.x NVENC API 13.1 (驱动 ≥610) 的规则过滤；
/// 在剩余版本中取最高者；若全被拦截则 <see cref="Recommended"/> 为 false，
/// <see cref="BlockReason"/> 给出拦截原因（一般是「N 卡驱动过旧」）。
/// </summary>
public sealed record FfmpegRecommendation(
    bool Recommended,
    string? RecommendedTag = null,
    string? RecommendedDownloadUrl = null,
    string? BlockReason = null,
    long RecommendedSizeBytes = 0);

/// <summary>单条版本是否应被允许推送（NVENC API 门槛）。</summary>
public sealed record FfmpegUpdateOffer(bool Offer, string? BlockReason = null);

/// <summary>
/// 软件更新（MarukoBox 自身，仅 GitHub）与内置 ffmpeg 的检查更新。
/// jellyfin-ffmpeg 版本号形如 "7.1.1-5"（tag），资产名
/// jellyfin-ffmpeg_{tag}-portable_win64.zip。
/// </summary>
public interface IUpdateService
{
    /// <summary>当前软件版本（程序集版本，如 "1.3.0"）。</summary>
    string GetAppVersion();

    /// <summary>当前内置 ffmpeg 的版本（读取 ffmpeg\VERSION 标记；未内置返回空字符串）。</summary>
    string GetLocalVersion();

    /// <summary>
    /// 从 GitHub 拉取 jellyfin-ffmpeg 全部 release（跳过 draft）的完整信息。
    /// 含 published_at、资产大小、是否 prerelease；供「程序员」专列版本列表与
    /// <see cref="GetRecommendedFfmpegAsync"/> 复用，避免重复请求。
    /// </summary>
    Task<IReadOnlyList<RemoteFfmpegRelease>> GetAllFfmpegReleasesAsync(CancellationToken ct = default);

    /// <summary>
    /// 按本机 GPU 驱动兼容性过滤，并在剩余版本中取最高者作为 ffmpeg 升级推荐。
    /// 若所有候选都因驱动门槛被拒，则返回 <see cref="FfmpegRecommendation.Recommended"/>=false
    /// 并在 <see cref="FfmpegRecommendation.BlockReason"/> 给出首个拦截原因。
    /// </summary>
    Task<FfmpegRecommendation> GetRecommendedFfmpegAsync(GpuInfo gpu, CancellationToken ct = default);

    /// <summary>
    /// 判定单条目标版本是否应被允许推送（NVENC API 门槛）。
    /// 程序员专列的版本列表里也用此方法给「兼容性」做软标注。
    /// </summary>
    FfmpegUpdateOffer ShouldOfferFfmpegUpdate(GpuInfo gpu, string targetTag);

    /// <summary>查询 MarukoBox 自身最新 Release（按 source 选 GitHub 或 Gitee 镜像）；查询失败抛出异常。</summary>
    Task<AppReleaseInfo> GetLatestAppReleaseAsync(UpdateSource source = UpdateSource.GitHub, CancellationToken ct = default);

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

    /// <summary>MarukoBox 仓库的 GitHub Releases 最新版 API。</summary>
    private const string AppRepoLatestApiGithub =
        "https://api.github.com/repos/294970144/maruko-box/releases/latest";

    /// <summary>MarukoBox 仓库的 Gitee 镜像最新版 API（公开仓库，匿名只读）。</summary>
    private const string AppRepoLatestApiGitee =
        "https://gitee.com/api/v5/repos/zhang-lin701442/maruko-box/releases/latest";

    private readonly HttpClient _http = new()
    {
        // 92MB 解压体 / 34MB 压缩包 / 97MB 安装包在慢速网络下需要足够长的下载窗口
        Timeout = TimeSpan.FromMinutes(10)
    };

    /// <summary>
    /// 软件安装包下载目录：用户专属的 <c>%LOCALAPPDATA%\MarukoBox\Updates\</c>。
    /// <para>
    /// v1.4.1 安全加固：此前落在 <c>%TEMP%</c>——该目录对同用户下的任何低完整性
    /// 进程都可写，下载完成到执行之间存在 TOCTOU 窗口，本地木马可替换 exe 实现
    /// 代码执行。LOCALAPPDATA 下本应用自建目录默认 ACL 仅当前用户可写。
    /// </para>
    /// </summary>
    private static string UpdatesDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarukoBox", "Updates");

    /// <summary>版本号白名单：仅允许数字、字母与 . + - ，杜绝路径穿越字符。</summary>
    private static readonly System.Text.RegularExpressions.Regex SafeVersionPattern =
        new(@"^[0-9A-Za-z.+\-]{1,32}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>当前软件版本（程序集版本，如 "1.2.0"）；静态版便于非服务上下文调用。</summary>
    public static string GetAppVersionStatic() =>
        typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <inheritdoc/>
    public string GetAppVersion() => GetAppVersionStatic();

    /// <inheritdoc/>
    public string GetLocalVersion() => ConfigService.GetBundledVersion();

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RemoteFfmpegRelease>> GetAllFfmpegReleasesAsync(CancellationToken ct = default)
        => await FetchAllReleasesAsync(ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<FfmpegRecommendation> GetRecommendedFfmpegAsync(GpuInfo gpu, CancellationToken ct = default)
    {
        var all = await FetchAllReleasesAsync(ct).ConfigureAwait(false);

        // 在通过兼容性门槛的版本中取最高；记录首个拦截原因以便汇总给用户。
        RemoteFfmpegRelease? best = null;
        string? firstBlock = null;
        foreach (var r in all)
        {
            var offer = ShouldOfferFfmpegUpdateStatic(gpu, r.Tag);
            if (!offer.Offer)
            {
                firstBlock ??= offer.BlockReason;
                continue;
            }
            if (best is null || CompareVersions(r.Tag, best.Tag) > 0)
            {
                best = r;
            }
        }

        return best is null
            ? new FfmpegRecommendation(false, BlockReason: firstBlock ?? "无可用的 ffmpeg 版本")
            : new FfmpegRecommendation(true, best.Tag, best.AssetUrl, null, best.AssetSizeBytes);
    }

    /// <summary>
    /// 拉取 jellyfin-ffmpeg 的全部 release（跳过 draft），序列化为
    /// <see cref="RemoteFfmpegRelease"/> 列表。
    /// 优先级：8.x 与 7.x 均入选（含 prerelease），这是为了确保 NVENC API 13.1
    /// 门槛与「程序员」专列版本列表都能看到完整候选。
    /// </summary>
    private async Task<List<RemoteFfmpegRelease>> FetchAllReleasesAsync(CancellationToken ct)
    {
        // jellyfin-ffmpeg 的 8.x 系列在 GitHub 上标为 prerelease（7.x 已停止更新，
        // 稳定版止于 7.1.4-3）。/releases/latest 只返回稳定版，会漏掉 8.x——
        // 而 8.x 恰恰是需要 NVENC API 13.1 门槛、且被此前镜像渠道选中的版本。
        // 因此这里拉取全部 release（跳过 draft），保证 NVENC 门槛与
        // 「内置 ffmpeg 更新」语义一致：始终能发现 8.x 并正确应用门槛。
        using var doc = await GetReleaseJsonAsync(
            "https://api.github.com/repos/jellyfin/jellyfin-ffmpeg/releases?per_page=30", ct)
            .ConfigureAwait(false);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("GitHub 返回的 ffmpeg Release 列表格式异常");
        }

        var result = new List<RemoteFfmpegRelease>();
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                continue; // 跳过草稿
            }

            var rawTag = release.TryGetProperty("tag_name", out var tn) ? tn.GetString() : null;
            var tag = NormalizeTag(rawTag ?? string.Empty);
            var publishedAt = ParsePublishedAt(release);
            var isPrerelease = release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();
            var (url, size) = FindPortableZipUrlWithSize(release);

            if (string.IsNullOrEmpty(tag) || url is null)
            {
                continue;
            }

            result.Add(new RemoteFfmpegRelease(tag, url, publishedAt, size, isPrerelease));
        }
        return result;
    }

    /// <summary>解析 release 的 published_at 字段；缺失或解析失败回退 UnixEpoch。</summary>
    private static DateTimeOffset ParsePublishedAt(JsonElement release)
    {
        if (release.TryGetProperty("published_at", out var pa) && pa.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(pa.GetString(), out var dto))
        {
            return dto;
        }
        return DateTimeOffset.UnixEpoch;
    }

    /// <summary>
    /// 在单个 release 的资产列表中查找 win64 便携版 zip 的下载地址与大小。
    /// 资产名随版本演进（旧: -portable_win64.zip；新: _portable_win64-clang-gpl.zip），
    /// 统一按「含 portable_win64 且以 .zip 结尾」匹配；portable_winarm64 不含该子串，不会误命中。
    /// </summary>
    private static (string? Url, long SizeBytes) FindPortableZipUrlWithSize(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return (null, 0);
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is not null
                && name.Contains("portable_win64", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                var size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number
                    ? s.GetInt64() : 0L;
                return (url, size);
            }
        }

        return (null, 0);
    }

    /// <inheritdoc/>
    public async Task<AppReleaseInfo> GetLatestAppReleaseAsync(UpdateSource source = UpdateSource.GitHub, CancellationToken ct = default)
    {
        var api = source == UpdateSource.Gitee ? AppRepoLatestApiGitee : AppRepoLatestApiGithub;
        using var doc = await GetReleaseJsonAsync(api, ct).ConfigureAwait(false);

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

        // 驱动版本未知：必须区分「本机没有 NVIDIA 硬件」与「有 N 卡但 nvidia-smi 不可用」。
        // v1.4.1 修复：此前两者一律放行，后者（驱动异常 / nvidia-smi 不在 PATH / WSL 环境）
        // 升级 8.x 后 NVENC 会静默失效且无任何提示。
        if (string.IsNullOrEmpty(gpu.DriverVersion) || gpu.DriverVersion == "未知")
        {
            if (gpu.HasNvencHevc || gpu.HasNvencH264)
            {
                return new FfmpegUpdateOffer(false,
                    "检测到本机有 NVENC 编码器，但未能读取 NVIDIA 驱动版本（nvidia-smi 不可用或不在 PATH）。"
                    + $"无法确认是否满足驱动 ≥{requiredDriver}，暂不推送 ffmpeg 8.x；"
                    + "请确认 nvidia-smi 可用后再检查更新，或以「专家」级从版本列表强制安装。");
            }

            // 确无 NVIDIA 硬件（AMD AMF / Intel QSV 无同类硬门槛）→ 放行
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

                    // 关键步骤：新版就位。只有这一步失败才算「安装失败」。
                    Directory.Move(tempExtract, BundledDir);

                    // v1.4.1 修复：旧备份的清理此前也在同一个 try 内，
                    // 一旦被杀毒软件占用导致删除失败，就会抛「无法替换内置 ffmpeg」，
                    // 而此时新版其实已经装好了——报错与真实状态矛盾。
                    // 清理失败不影响结果，残留由 ConfigService.RecoverBundledBackup 自愈。
                    try
                    {
                        Directory.Delete(backup, recursive: true);
                    }
                    catch
                    {
                        // 下次启动自愈，忽略
                    }
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
        // v1.4.1 安全加固（S2）：version 直接来自 GitHub 返回的 tag_name，
        // 旧实现只剥掉 v 前缀就拼进文件名，tag 若为 "..\..\Startup\evil"
        // 就能把文件写到任意位置。这里做白名单校验，不合法直接拒绝下载。
        if (!SafeVersionPattern.IsMatch(version ?? string.Empty))
        {
            throw new InvalidOperationException(
                $"Release 版本号 \"{version}\" 含非法字符，已中止更新。");
        }

        // v1.4.1 安全加固（S1）：落盘到用户专属目录，而非全局可写的 %TEMP%。
        var dir = UpdatesDirectory;
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, $"MarukoBoxSetup_{version}.exe");

        await DownloadFileAsync(downloadUrl, dest, progress, ct).ConfigureAwait(false);

        // 下载完成后的基本完整性检查：文件必须存在且非空，避免把 0 字节 / 半截文件当安装包执行。
        var info = new FileInfo(dest);
        if (!info.Exists || info.Length <= 0)
        {
            TryCleanup(dest);
            throw new InvalidOperationException("安装包下载失败（文件为空），已中止更新。");
        }

        return dest;
    }

    /// <summary>
    /// 清理历史遗留安装包：删除 Updates 目录下除了刚下载的这个文件以外的旧安装包。
    /// 由调用方在安装成功启动后调用，避免长期堆积几十 MB 的 exe。
    /// </summary>
    public static void CleanupOldInstallers(string? keepFile)
    {
        try
        {
            var dir = UpdatesDirectory;
            if (!Directory.Exists(dir))
            {
                return;
            }

            var keep = string.IsNullOrEmpty(keepFile)
                ? null
                : Path.GetFullPath(keepFile);

            foreach (var file in Directory.EnumerateFiles(dir, "MarukoBoxSetup_*.exe"))
            {
                if (keep is not null
                    && string.Equals(Path.GetFullPath(file), keep, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TryCleanup(file);
            }
        }
        catch
        {
            // 清理失败不影响更新流程
        }
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
