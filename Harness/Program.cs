using MarukoBox.Services;
using MarukoBox.Models;
using System.Diagnostics;

// 场景开关：`update` = 更新链路冒烟（软件更新查询 + NVENC 门槛矩阵 + ffmpeg 更新）；
// `recover` = 更新中断自愈冒烟；默认 = 编码冒烟
if (args.Any(a => a.Equals("update", StringComparison.OrdinalIgnoreCase)))
{
    return await UpdateSmokeAsync();
}
if (args.Any(a => a.Equals("recover", StringComparison.OrdinalIgnoreCase)))
{
    return RecoverSmokeAsync();
}
if (args.Any(a => a.Equals("trim", StringComparison.OrdinalIgnoreCase)))
{
    return await TrimSmokeAsync(args);
}

// ---------- B1 回归：质量四档必须真正落地为不同 CRF/CQP（纯逻辑，不依赖 ffmpeg） ----------
// v1.4.0 该四档在 GPU 路径下完全失效（只写 Quality 但 RateControl 仍 vbr，四档输出体积相同）。
// v1.4.1 由 QualityPresets.Apply 同步切换 cqp/crf 模式——这里直接断言两条路径的参数都正确。
Console.WriteLine("=== B1 回归：质量四档落地校验（v1.4.1 修复）===");
Console.Out.Flush();
var presetFails = 0;
foreach (var (preset, expect) in new (string Preset, int Expect)[]
         {
             (QualityPresets.Low, 30),
             (QualityPresets.Medium, 26),
             (QualityPresets.High, 22),
             (QualityPresets.VeryHigh, 18),
         })
{
    var s = new EncodeSettings();
    QualityPresets.Apply(s, preset);
    var crfOk = s.Crf == expect && s.Quality == expect;
    var modeOk = s.RateControl == "cqp" && s.CpuMode == "crf";

    // GPU（NVENC）路径：必须落到 -rc constqp -qp <expect>
    var gpuArgs = new FfmpegService().BuildArguments(
        s, EncoderType.NvencHevc, new GpuInfo { HasCudaScale = true });
    var gpuOk = gpuArgs.Contains($"-rc constqp -qp {expect}", StringComparison.Ordinal);

    // CPU（x264）路径：必须落到 -crf <expect>
    var cpuArgs = new FfmpegService().BuildArguments(s, EncoderType.X264, new GpuInfo());
    var cpuOk = cpuArgs.Contains($"-crf {expect}", StringComparison.Ordinal);

    var pass = crfOk && modeOk && gpuOk && cpuOk;
    if (!pass) presetFails++;
    Console.WriteLine($"  {(pass ? "PASS" : "FAIL")} preset={preset} expect={expect} " +
                      $"crf={s.Crf} qp={s.Quality} rc={s.RateControl} " +
                      $"gpu={(gpuOk ? "ok" : "BAD")} cpu={(cpuOk ? "ok" : "BAD")}");
}
Console.WriteLine($"  B1 质量四档: {(presetFails == 0 ? "PASS" : $"FAIL ({presetFails} 项)")}");

// ---------- B1' 回归：-gpu 只能给 NVENC ----------
// v1.7.0 修复：此前 -gpu 对 Nvenc/Amf/Qsv 一视同仁地追加，而该参数只有 NVENC 认识，
// AMD / Intel 用户在设置页填了设备号就会每次编码都报 "Option not found" 失败。
Console.WriteLine("=== B1' 回归：-gpu 只给 NVENC ===");
Console.Out.Flush();
var gpuFails = 0;

EncodeSettings SettingsWith(int device) => new() { GpuDevice = device };

foreach (var (type, label) in new (EncoderType Type, string Label)[]
         {
             (EncoderType.NvencHevc, "NVENC HEVC"),
             (EncoderType.NvencH264, "NVENC H.264"),
             (EncoderType.AmfHevc, "AMD AMF"),
             (EncoderType.QsvHevc, "Intel QSV"),
             (EncoderType.X264, "x264 CPU")
         })
{
    var encArgs = new FfmpegService().BuildArguments(
        SettingsWith(1), type, new GpuInfo { HasCudaScale = true });

    var isNvenc = type is EncoderType.NvencHevc or EncoderType.NvencH264;
    var hasGpuFlag = encArgs.Contains("-gpu ", StringComparison.Ordinal);
    var gpuOk = hasGpuFlag == isNvenc;

    if (!gpuOk)
    {
        gpuFails++;
    }

    Console.WriteLine($"  {(gpuOk ? "PASS" : "FAIL")} {label}: -gpu {(hasGpuFlag ? "有" : "无")}（应为 {(isNvenc ? "有" : "无")}）");
}

// 设备号 0 = 自动，任何编码器都不该出现 -gpu
var zeroArgs = new FfmpegService().BuildArguments(
    SettingsWith(0), EncoderType.NvencHevc, new GpuInfo { HasCudaScale = true });
var zeroOk = !zeroArgs.Contains("-gpu ", StringComparison.Ordinal);
if (!zeroOk)
{
    gpuFails++;
}

Console.WriteLine($"  {(zeroOk ? "PASS" : "FAIL")} 设备号 0（自动）时不追加 -gpu");
Console.WriteLine($"  B1' -gpu 限定: {(gpuFails == 0 ? "PASS" : $"FAIL ({gpuFails} 项)")}");
Console.Out.Flush();
Console.Out.Flush();

// 与主程序一致的路径解析链：内置 ffmpeg 优先，其次 PATH。
//
// 【保真度】Harness 从自己的 bin 目录启动，那里没有 ffmpeg\ 子目录，
// 于是 BundledFfmpegPath 不存在 → 回落到 PATH → 实测抓到的是 **winget 的
// ffmpeg-N-124279**（内置版本标记为空），而不是产品内置的 jellyfin-7.1.1-5。
// 两者选项集不同，C2 这类"参数名/取值是否被接受"的问题可能在错误的目标上被验证通过。
// 因此：① 支持 MB_FFMPEG 环境变量显式指定；② 未用内置版时打印醒目告警。
var ffmpeg = Environment.GetEnvironmentVariable("MB_FFMPEG");
if (string.IsNullOrWhiteSpace(ffmpeg))
{
    ffmpeg = ConfigService.ResolveFfmpegPath();
}

if (string.IsNullOrEmpty(ffmpeg))
{
    Console.WriteLine("未找到 ffmpeg（无内置、PATH 中也没有）。请放置内置 ffmpeg 或将其加入 PATH，或用 MB_FFMPEG 指定。");
    Environment.Exit(1);
}

Console.WriteLine($"ffmpeg: {ffmpeg}");
Console.WriteLine($"内置版本标记: {ConfigService.GetBundledVersion()}");
if (!string.Equals(ffmpeg, ConfigService.BundledFfmpegPath, StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("⚠ 注意：当前用的不是产品内置 ffmpeg（Harness 自身目录无内置副本）。");
    Console.WriteLine("  编码器/参数断言请指定 MB_FFMPEG 指向产品内置的 jellyfin ffmpeg，");
    Console.WriteLine("  否则验证的是另一个构建的选项集，结论可能不适用于产品。");
}
Console.Out.Flush();

// 【M8 修复】测试素材路径不再硬编码到某个人的目录：
// 优先读环境变量 MB_TEST_SRC / MB_TEST_OUT，缺省才回落到原来的路径。
// 换机器跑冒烟时：  $env:MB_TEST_SRC = "D:\test\src.mp4"
var src = Environment.GetEnvironmentVariable("MB_TEST_SRC") ?? @"C:\Users\zhang\fftest\src.mp4";
var outp = Environment.GetEnvironmentVariable("MB_TEST_OUT") ?? @"C:\Users\zhang\fftest\out.mp4";
Directory.CreateDirectory(Path.GetDirectoryName(src)!);

Console.WriteLine("=== 生成测试源 ===");
Console.Out.Flush();
var gen = Process.Start(new ProcessStartInfo(ffmpeg,
    $"-y -f lavfi -i \"testsrc=duration=20:size=1280x720:rate=30\" -c:v libx264 -pix_fmt yuv420p \"{src}\"")
{
    UseShellExecute = false,
    CreateNoWindow = true
});
gen!.WaitForExit();
Console.WriteLine($"源文件大小: {new FileInfo(src).Length}");
Console.Out.Flush();

Console.WriteLine("=== 检测 GPU ===");
var gpu = await new GpuDetectionService().DetectAsync(ffmpeg);
Console.WriteLine($"found={gpu.FfmpegFound} nvenc={gpu.HasNvencHevc} cudaScale={gpu.HasCudaScale} cudaDecode={gpu.HasCudaDecode}");

Console.WriteLine("=== 执行编码（复刻 VideoViewModel.StartAsync 的真实调用）===");
var settings = new EncodeSettings
{
    InputPath = src,
    OutputPath = outp,
    Encoder = EncoderType.Auto
};
var prog = new Progress<EncodeProgress>(p =>
    Console.WriteLine($"  progress: {p.Percent:F1}%  speed={p.Speed}x  fps={p.Fps}"));

bool ok = false;
try
{
    ok = await new FfmpegService().EncodeAsync(settings, gpu, prog, CancellationToken.None);
    Console.WriteLine($"=== 编码结果: {(ok ? "成功" : "失败")} ===");
}
catch (Exception ex)
{
    Console.WriteLine($"=== 编码抛出异常（这就是闪退根因）===");
    Console.WriteLine($"{ex.GetType().FullName}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

// =====================================================================
// 【三个 Critical 的回归断言：真跑 ffmpeg，而不是只比对字符串形状】
//
// C1 / C2 / C3 共享同一根因：编码器参数构建**从未与真实 ffmpeg 后端端到端验证过**。
// 上面那些断言校验的是 BuildArguments 返回的字符串**长得对不对**，
// 而 ffmpeg 是否接受这个字符串没人管——所以 `-keyint`（x264 命令行的参数名，
// ffmpeg 侧叫 -g）与 NVENC 私有的 `-rc vbr` / `-preset p4` 能一路潜伏到线上。
//
// 这里对每种编码器用真实 ffmpeg 编 2 帧到 null 输出，判据是**基线对照**，不是关键字猜测：
//
//   1. 先跑「基线」：只用 -c:v {codec}，不带 hwaccel / 输出格式 / 滤镜 / 我们拼的任何参数。
//   2. 再跑「全参」：BuildCoreArguments 的真实产物。
//   3. 判定：
//        全参 exit 0              → PASS
//        全参失败 + 基线成功      → **FAIL**（基线证明硬件可用，失败只可能是我们拼的参数组合错）
//        全参失败 + 基线也失败    → SKIP（本机硬件/驱动不可用，换机器可能就过）
//
// 【为什么不用关键字白名单】第一版用 4 个关键字（Unrecognized option / Option not found /
// Error setting option / Error applying encoder options）判断"参数被拒"，结果把
// `hevc_amf + -hwaccel_output_format cuda` 的 `Function not implemented` /
// `Error reinitializing filters` 记成了 SKIP——在唯一能测 AMF 的机器上给了**假绿**。
// 关键字永远追不上 ffmpeg 的报错措辞；"基线能过、全参过不了"才是可靠的判别式。
// =====================================================================
Console.WriteLine("=== 编码器端到端断言（真跑 ffmpeg · 每种编 2 帧到 null）===");
Console.Out.Flush();

var encoderFails = 0;
var buildCore = typeof(FfmpegService).GetMethod(
    "BuildCoreArguments",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

if (buildCore is null)
{
    Console.WriteLine("  FAIL 反射不到 BuildCoreArguments，端到端断言无法执行");
    encoderFails++;
}
else
{
    foreach (var enc in new[]
             {
                 EncoderType.NvencHevc, EncoderType.AmfHevc, EncoderType.QsvHevc,
                 EncoderType.X264, EncoderType.X265
             })
    {
        var codec = enc.ToFfmpegCodec();

        // ---- 基线：只指定编码器，不带任何我们拼的参数（hwaccel / 输出格式 / 滤镜 / 码率…）----
        var (baselineCode, _) = RunFfmpeg(ffmpeg,
            $"-hide_banner -loglevel error -f lavfi -i testsrc=s=320x240:d=1:r=25 " +
            $"-c:v {codec} -frames:v 2 -f null -");

        // ---- 全参：BuildCoreArguments 的真实产物 ----
        var s = new EncodeSettings { InputPath = src, OutputPath = outp, Encoder = enc };
        var core = (string)buildCore.Invoke(null, new object[] { s, enc, gpu })!;
        var (code, err) = RunFfmpeg(ffmpeg, core.TrimEnd() + " -frames:v 2 -f null -");

        if (code == 0)
        {
            Console.WriteLine($"  PASS {enc}");
        }
        else if (baselineCode != 0)
        {
            // 基线也过不了 → 本机没有可用硬件/驱动，不是我们参数的问题
            Console.WriteLine($"  SKIP {enc}：本机不可用（基线退出码 {baselineCode}、全参 {code}）");
        }
        else
        {
            // 基线能编、全参编不了 → 只可能是我们拼的参数组合有问题（C1/C2/C3 连带形态）
            encoderFails++;
            Console.WriteLine($"  FAIL {enc}：基线可编（exit 0）但全参失败（exit {code}）→ {FirstErrorLine(err)}");
        }

        Console.Out.Flush();
    }
}

// 跑一次 ffmpeg，返回 (退出码, stderr)
static (int Code, string Err) RunFfmpeg(string ffmpegPath, string arguments)
{
    var psi = new ProcessStartInfo(ffmpegPath, arguments)
    {
        UseShellExecute = false,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        CreateNoWindow = true,
        StandardErrorEncoding = System.Text.Encoding.UTF8,
        StandardOutputEncoding = System.Text.Encoding.UTF8
    };

    using var p = Process.Start(psi)!;
    var err = p.StandardError.ReadToEnd();
    p.StandardOutput.ReadToEnd();   // 抽干 stdout，避免缓冲区满导致进程挂住
    p.WaitForExit();
    return (p.ExitCode, err);
}

Console.WriteLine($"  编码器端到端: {(encoderFails == 0 ? "PASS" : $"FAIL ({encoderFails} 项)")}");

return presetFails == 0 && ok && encoderFails == 0 ? 0 : 1;

// 取第一条"看起来是错误原因"的行，便于一眼定位。
// 只用于**展示**：判定由上面的「基线对照」负责，不再依赖关键字白名单。
static string FirstErrorLine(string stderr)
{
    foreach (var raw in stderr.Split('\n'))
    {
        var line = raw.Trim();
        if (line.Length == 0)
        {
            continue;
        }

        if (line.Contains("Error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Invalid", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Unrecognized", StringComparison.OrdinalIgnoreCase)
            || line.Contains("not implemented", StringComparison.OrdinalIgnoreCase))
        {
            return line.Length > 160 ? line[..160] + "…" : line;
        }
    }

    return stderr.Trim().Length == 0 ? "(stderr 为空)" : stderr.Trim()[..Math.Min(160, stderr.Trim().Length)];
}

// ---------- 更新链路冒烟：直接执行 UpdateService 产品代码 ----------
static async Task<int> UpdateSmokeAsync()
{
    IUpdateService update = new UpdateService();

    Console.WriteLine("=== 更新链路冒烟（GitHub-only） ===");
    Console.Out.Flush();
    Console.WriteLine($"本地内置版本: {update.GetLocalVersion() ?? "(无)"}");
    Console.WriteLine($"软件版本(assembly): {update.GetAppVersion()}");

    // 1) NVENC 门槛判定矩阵（纯逻辑，不依赖网络）
    Console.WriteLine("\n--- NVENC API 门槛判定矩阵 ---");
    Console.Out.Flush();
    var cases = new (GpuInfo Gpu, string Tag, bool Expect, string Note)[]
    {
        (new GpuInfo { DriverVersion = "610.62", NvencApiVersion = "13.1" }, "8.1.2-3", true,  "驱动610 + 目标8.x → 推送"),
        (new GpuInfo { DriverVersion = "591.86", NvencApiVersion = "13.0" }, "8.1.2-3", false, "驱动591 + 目标8.x → 不推送"),
        (new GpuInfo { DriverVersion = "550.40", NvencApiVersion = "12.2" }, "8.1.2-3", false, "驱动550 + 目标8.x → 不推送"),
        (new GpuInfo { DriverVersion = "未知",  NvencApiVersion = "未知" }, "8.1.2-3", true,  "无 N 卡/无法判定 → 推送"),
        (new GpuInfo { DriverVersion = "591.86", NvencApiVersion = "13.0" }, "7.1.2-3", true,  "目标 7.x → 无门槛推送"),
        (new GpuInfo { DriverVersion = "530.10", NvencApiVersion = "12.1" }, "v8.1.2-3", false, "带 v 前缀 tag 解析 → 不推送"),
    };
    var matrixFail = 0;
    foreach (var c in cases)
    {
        var offer = UpdateService.ShouldOfferFfmpegUpdateStatic(c.Gpu, c.Tag);
        var pass = offer.Offer == c.Expect;
        if (!pass)
        {
            matrixFail++;
        }
        Console.WriteLine($"  {(pass ? "PASS" : "FAIL")} [驱动={c.Gpu.DriverVersion} 目标={c.Tag}] " +
                          $"expect={c.Expect} got={offer.Offer}  ({c.Note})");
        if (!offer.Offer)
        {
            Console.WriteLine($"       原因: {offer.BlockReason}");
        }
    }
    Console.Out.Flush();

    // 2) 软件更新查询（GitHub maruko-box latest）
    Console.WriteLine("\n--- 软件更新查询（GitHub 294970144/maruko-box） ---");
    Console.Out.Flush();
    try
    {
        var app = await update.GetLatestAppReleaseAsync();
        Console.WriteLine($"远端最新: tag={app.Tag} version={app.Version}");
        Console.WriteLine($"下载地址: {app.DownloadUrl}");
        var cmp = UpdateService.CompareVersions(update.GetAppVersion(), app.Version);
        Console.WriteLine($"本地 {update.GetAppVersion()} vs 远端 {app.Version} -> " +
            (cmp >= 0 ? "已是最新" : "发现新版本"));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FAIL] 软件更新查询失败: {ex.Message}");
        return 1;
    }

    // 3) 内置 ffmpeg 更新查询（GitHub jellyfin-ffmpeg）+ 真实 GPU 门槛判定 + 推荐
    Console.WriteLine("\n--- 内置 ffmpeg 更新（GitHub jellyfin/jellyfin-ffmpeg） ---");
    Console.WriteLine("\n--- v1.3.0: UserLevels 改名 + 兼容映射 ---");
    Console.Out.Flush();
    var umCases = new (string Display, string ExpectCode)[]
    {
        ("普通", "default"),
        ("高级", "expert"),
        ("专家", "developer"),
        // v1.2.0 旧显示名仍兼容（防止既有用户升级后被识别为「default」）
        ("默认", "default"),
        ("高手", "expert"),
        ("程序员", "developer"),
    };
    var umFails = 0;
    foreach (var c in umCases)
    {
        var code = UserLevels.DisplayToCode(c.Display);
        var back = UserLevels.ToDisplay(UserLevels.Parse(code));
        var pass = code == c.ExpectCode && back == c.Display;
        if (!pass) umFails++;
        Console.WriteLine($"  {(pass ? "PASS" : "FAIL")} DisplayToCode(\"{c.Display}\") = \"{code}\" (期望 \"{c.ExpectCode}\") → 回显 \"{back}\"");
    }
    Console.WriteLine($"  v1.3.0 UserLevel 映射: {(umFails == 0 ? "PASS" : "FAIL ({umFails} 项)")}");

    // 4) AppConfig.AfterCompletion 字段弃用：旧 JSON 含该字段时 Load 不会报错（System.Text.Json 默认忽略）
    Console.WriteLine("\n--- v1.3.0: AppConfig.AfterCompletion 反向兼容 ---");
    Console.Out.Flush();
    var oldCfgJson = "{\"AfterCompletion\":\"shutdown\",\"Theme\":\"Dark\",\"UserLevel\":\"developer\"}";
    try
    {
        var probe = System.Text.Json.JsonSerializer.Deserialize<MarukoBox.Services.AppConfig>(oldCfgJson);
        // AfterCompletion 字段已删，探针对象只能取剩下的字段（实际拿不到 AfterCompletion，但 Deserialize 不抛即可）
        var ok = probe is not null
                 && probe.Theme == "Dark"
                 && probe.UserLevel == "developer";
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")} 旧 JSON 含 AfterCompletion 字段 → Deserialize 不抛异常、被忽略，其余字段正确");
        if (!ok) umFails++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAIL  旧 JSON 解析异常: {ex.Message}");
        umFails++;
    }

    // 5) 内置 ffmpeg 更新（拉全部 release + 推荐兼容 + 真实下载安装）
    Console.WriteLine("\n--- 内置 ffmpeg 更新（拉全量 + 驱动兼容推荐）---");
    Console.Out.Flush();
    IReadOnlyList<RemoteFfmpegRelease> releases;
    try
    {
        releases = await update.GetAllFfmpegReleasesAsync();
        Console.WriteLine($"拉到 {releases.Count} 个 release（按 tag 倒序示例）:");
        foreach (var r in releases.Take(5))
        {
            Console.WriteLine($"  tag={r.Tag}  prerelease={r.IsPrerelease}  size={r.AssetSizeBytes / 1024 / 1024}MB  published={r.PublishedAt:yyyy-MM-dd}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FAIL] 拉取 ffmpeg 列表失败: {ex.Message}");
        return 1;
    }

    var ffmpegPath = ConfigService.ResolveFfmpegPath();
    GpuInfo realGpu = new();
    if (!string.IsNullOrEmpty(ffmpegPath))
    {
        realGpu = await new GpuDetectionService().DetectAsync(ffmpegPath);
        Console.WriteLine($"真实 GPU: {realGpu.GpuName} 驱动 {realGpu.DriverVersion} NVENC API {realGpu.NvencApiVersion}");
    }

    var rec = await update.GetRecommendedFfmpegAsync(realGpu);
    Console.WriteLine($"按驱动兼容性推荐: recommended={rec.Recommended}" +
        (rec.Recommended ? $" tag={rec.RecommendedTag}" : "") +
        (rec.BlockReason is null ? string.Empty : $" 拦截原因={rec.BlockReason}"));

    var localFfmpeg = update.GetLocalVersion();
    Console.WriteLine($"本地内置: {(string.IsNullOrEmpty(localFfmpeg) ? "(无)" : localFfmpeg)}");

    if (!rec.Recommended)
    {
        Console.WriteLine("本地驱动被门槛拦截 → 不下载（符合预期）");
        Console.WriteLine($"\n=== 更新链路冒烟: {(matrixFail == 0 && umFails == 0 ? "成功" : "失败")} ===");
        Console.Out.Flush();
        return (matrixFail == 0 && umFails == 0) ? 0 : 1;
    }

    var recommendedTag = rec.RecommendedTag!;
    var recommendedUrl = rec.RecommendedDownloadUrl!;

    // 6) 本地落后且门槛通过时，执行真实下载安装（整目录替换）
    if (string.IsNullOrEmpty(localFfmpeg) || UpdateService.CompareVersions(localFfmpeg, recommendedTag) < 0)
    {
        Console.WriteLine("\n--- 下载并安装（按推荐版本，整目录替换） ---");
        Console.Out.Flush();
        var progress = new Progress<MarukoBox.Services.DownloadProgress>(p =>
        {
            var pct = p.BytesTotal > 0 ? $" {p.BytesDone * 100.0 / p.BytesTotal:F0}%" : string.Empty;
            Console.Write($"\r下载进度: {p.BytesDone / 1024d / 1024d:F1} MB{pct}   ");
        });
        try
        {
            await update.DownloadAndInstallAsync(recommendedUrl, recommendedTag, progress);
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] 下载/安装失败: {ex.Message}");
            return 1;
        }

        var newVer = update.GetLocalVersion();
        Console.WriteLine($"安装后 VERSION 标记: {newVer}");
        Console.WriteLine($"安装后 ffmpeg.exe 存在: {File.Exists(ConfigService.BundledFfmpegPath)}");

        var psi = new ProcessStartInfo(ConfigService.BundledFfmpegPath, "-version")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)!;
        var firstLine = proc.StandardOutput.ReadLine();
        proc.WaitForExit();
        Console.WriteLine($"ffmpeg -version: {firstLine}");

        var ok = newVer == recommendedTag && proc.ExitCode == 0 && matrixFail == 0 && umFails == 0;
        Console.WriteLine($"\n=== 更新链路冒烟: {(ok ? "成功" : "失败")} ===");
        Console.Out.Flush();
        return ok ? 0 : 1;
    }

    Console.WriteLine("本地已是最新，跳过下载。");
    var done = matrixFail == 0 && umFails == 0;
    Console.WriteLine($"\n=== 更新链路冒烟: {(done ? "成功" : "失败")} ===");
    Console.Out.Flush();
    return done ? 0 : 1;
}

// ---------- 更新中断自愈冒烟：直接制造中断现场，验证 RecoverBundledBackup ----------
// RecoverBundledBackup 是 private 且仅由 ConfigService.Load() 触发，
// 此场景通过 Load() 间接执行它，验证两种中断现场都能自愈。
static int RecoverSmokeAsync()
{
    var appDir = AppContext.BaseDirectory;
    var current = Path.Combine(appDir, "ffmpeg");
    var backup = current + ".old";
    var fails = 0;

    Console.WriteLine("=== 更新中断自愈冒烟 ===");
    Console.Out.Flush();

    // 现场 A：备份残留且新版未就位（ffmpeg\ 缺失、ffmpeg.old 存在）
    //   → 自愈应把 ffmpeg.old 移回 ffmpeg\
    Console.WriteLine("\n[场景 A] 中断于「旧目录已改名 .old、新版未移入」");
    Console.Out.Flush();
    if (!Directory.Exists(current))
    {
        Console.WriteLine("  前置条件不满足（当前无 ffmpeg\\），跳过。");
        return 2;
    }
    Directory.Move(current, backup);
    Console.WriteLine($"  已制造现场: ffmpeg\\ 缺失, ffmpeg.old 存在 -> {Directory.Exists(backup)}");
    _ = new ConfigService().Load();  // 触发 RecoverBundledBackup
    var aOk = Directory.Exists(current) && File.Exists(Path.Combine(current, "ffmpeg.exe")) && !Directory.Exists(backup);
    Console.WriteLine($"  自愈后 ffmpeg\\ 恢复: {Directory.Exists(current)}, .old 清除: {!Directory.Exists(backup)} -> {(aOk ? "PASS" : "FAIL")}");
    if (!aOk) fails++;

    // 现场 B：备份残留但新版已就位（ffmpeg\ 和 ffmpeg.old 同时存在）
    //   → 自愈应删除 .old 残留
    Console.WriteLine("\n[场景 B] 中断于「新版已移入、备份未删」");
    Console.Out.Flush();
    Directory.CreateDirectory(backup);
    Console.WriteLine($"  已制造现场: ffmpeg\\ 和 ffmpeg.old 同时存在 -> {Directory.Exists(current) && Directory.Exists(backup)}");
    _ = new ConfigService().Load();
    var bOk = Directory.Exists(current) && File.Exists(Path.Combine(current, "ffmpeg.exe")) && !Directory.Exists(backup);
    Console.WriteLine($"  自愈后 ffmpeg\\ 保留: {Directory.Exists(current)}, .old 清除: {!Directory.Exists(backup)} -> {(bOk ? "PASS" : "FAIL")}");
    if (!bOk) fails++;

    Console.WriteLine($"\n=== 更新中断自愈冒烟: {(fails == 0 ? "成功" : $"失败 {fails} 项")} ===");
    Console.Out.Flush();
    return fails == 0 ? 0 : 1;
}

// ---------- 裁剪冒烟 ----------
// 裁剪的坑几乎全在参数顺序上：-ss 放在 -i 前后语义不同；前置 -ss 时 -to 会少算一个 start；
// -avoid_negative_ts make_zero 会连带关掉拷贝模式的时间戳截断（实测 5 秒请求产出 6.59 秒）。
// 这些只靠"编译过"发现不了，所以这里既断言参数字符串，也拿真实文件各跑一遍核对时长。
async Task<int> TrimSmokeAsync(string[] argv)
{
    var rest = argv.Where(a => !a.Equals("trim", StringComparison.OrdinalIgnoreCase)).ToList();
    var input = rest.ElementAtOrDefault(0) ?? string.Empty;
    var start = double.TryParse(rest.ElementAtOrDefault(1), out var s) ? s : 1.5;
    var dur = double.TryParse(rest.ElementAtOrDefault(2), out var d) ? d : 5.0;

    Console.WriteLine("=== 裁剪冒烟 ===");
    Console.Out.Flush();

    var fails = 0;
    void Check(string name, bool ok, string? detail = null)
    {
        if (!ok)
        {
            fails++;
        }

        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")} {name}{(detail is null ? "" : "  " + detail)}");
        Console.Out.Flush();
    }

    if (!File.Exists(input))
    {
        Console.WriteLine($"  用法: MarukoBox.Harness.exe trim <视频路径> [起点秒] [时长秒]");
        Console.WriteLine($"  输入不存在: {input}");
        return 2;
    }

    var ffmpeg = ConfigService.ResolveFfmpegPath();
    if (string.IsNullOrEmpty(ffmpeg))
    {
        Console.WriteLine("  未找到 ffmpeg，无法冒烟。");
        return 2;
    }

    // ffprobe 优先取 ffmpeg 同目录，其次交给 PATH
    var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
    if (!File.Exists(ffprobe))
    {
        ffprobe = "ffprobe";
    }

    var svc = new FfmpegService();
    var gpu = new GpuInfo();   // 冒烟不依赖显卡：精确模式显式指定 CPU 编码器
    var outDir = Path.Combine(Path.GetTempPath(), "marukobox-trim-smoke");
    Directory.CreateDirectory(outDir);

    // ---- 1. 参数断言 ----
    var copyReq = new TrimRequest
    {
        InputPath = input,
        OutputPath = Path.Combine(outDir, "copy.mp4"),
        Start = TimeSpan.FromSeconds(start),
        End = TimeSpan.FromSeconds(start + dur),
        Mode = TrimMode.Copy
    };
    var copyArgs = svc.BuildTrimArguments(copyReq, gpu);
    Console.WriteLine($"  copy 参数: {copyArgs}");
    Check("copy: 起点用 -ss", copyArgs.Contains($"-ss {start:0.###}", StringComparison.Ordinal));
    Check("copy: 时长用 -t", copyArgs.Contains($"-t {dur:0.###}", StringComparison.Ordinal));
    Check("copy: 全轨拷贝", copyArgs.Contains("-map 0 -c copy", StringComparison.Ordinal));
    Check("copy: 不含 -avoid_negative_ts（会让 -t 失效）",
        !copyArgs.Contains("avoid_negative_ts", StringComparison.Ordinal));

    var encReq = new TrimRequest
    {
        InputPath = input,
        OutputPath = Path.Combine(outDir, "reenc.mp4"),
        Start = TimeSpan.FromSeconds(start),
        End = TimeSpan.FromSeconds(start + dur),
        Mode = TrimMode.ReEncode,
        Encoder = EncoderType.X264,
        Quality = 20
    };
    var encArgs = svc.BuildTrimArguments(encReq, gpu);
    Console.WriteLine($"  重编码参数: {encArgs}");
    Check("重编码: libx264 + crf 20",
        encArgs.Contains("-c:v libx264", StringComparison.Ordinal) &&
        encArgs.Contains("-crf 20", StringComparison.Ordinal));
    Check("重编码: 音轨拷贝", encArgs.Contains("-c:a copy", StringComparison.Ordinal));

    // ---- 2. 真跑一遍，核对输出时长 ----
    var progress = new Progress<EncodeProgress>(p =>
    {
        if (p.HasError)
        {
            Console.WriteLine($"    [错误] {p.ErrorMessage}");
        }
    });

    var okCopy = await svc.TrimAsync(ffmpeg, copyReq, gpu, progress);
    var okEnc = await svc.TrimAsync(ffmpeg, encReq, gpu, progress);
    Check("copy 模式执行成功", okCopy && File.Exists(copyReq.OutputPath));
    Check("重编码模式执行成功", okEnc && File.Exists(encReq.OutputPath));

    var durCopy = await ProbeDurationAsync(ffprobe, copyReq.OutputPath);
    var durEnc = await ProbeDurationAsync(ffprobe, encReq.OutputPath);

    // 允许 0.2s 误差：音频帧粒度与容器时间戳会带一点尾巴
    Check($"copy 输出时长≈{dur:0.###}s", Math.Abs(durCopy - dur) <= 0.2, $"实际 {durCopy:0.###}s");
    Check($"重编码输出时长≈{dur:0.###}s", Math.Abs(durEnc - dur) <= 0.2, $"实际 {durEnc:0.###}s");

    Console.WriteLine($"=== 裁剪冒烟: {(fails == 0 ? "全部通过" : $"失败 {fails} 项")} ===");
    Console.Out.Flush();
    return fails == 0 ? 0 : 1;
}

static async Task<double> ProbeDurationAsync(string ffprobe, string file)
{
    if (!File.Exists(file))
    {
        return -1;
    }

    var psi = new ProcessStartInfo
    {
        FileName = ffprobe,
        Arguments = $"-v error -show_entries format=duration -of csv=p=0 \"{file}\"",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    using var p = Process.Start(psi);
    if (p is null)
    {
        return -1;
    }

    var text = await p.StandardOutput.ReadToEndAsync();
    await p.WaitForExitAsync();

    return double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1;
}
