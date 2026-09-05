using MarukoBox.Services;
using MarukoBox.Models;
using System.Diagnostics;

// 场景开关：`update` = 更新链路冒烟（渠道查询 + 下载 + 安装替换）；`recover` = 更新中断自愈冒烟；
// 默认 = 编码冒烟
if (args.Any(a => a.Equals("update", StringComparison.OrdinalIgnoreCase)))
{
    return await UpdateSmokeAsync();
}
if (args.Any(a => a.Equals("recover", StringComparison.OrdinalIgnoreCase)))
{
    return RecoverSmokeAsync();
}

// 与主程序一致的路径解析链：内置 ffmpeg 优先，其次 PATH
var ffmpeg = ConfigService.ResolveFfmpegPath();
if (string.IsNullOrEmpty(ffmpeg))
{
    Console.WriteLine("未找到 ffmpeg（无内置、PATH 中也没有）。请放置内置 ffmpeg 或将其加入 PATH。");
    Environment.Exit(1);
}
Console.WriteLine($"ffmpeg: {ffmpeg}");
Console.WriteLine($"内置版本标记: {ConfigService.GetBundledVersion()}");

var src = @"C:\Users\zhang\fftest\src.mp4";
var outp = @"C:\Users\zhang\fftest\out.mp4";
Directory.CreateDirectory(Path.GetDirectoryName(src)!);

Console.WriteLine("=== 生成测试源 ===");
Console.Out.Flush();
var gen = Process.Start(new ProcessStartInfo(ffmpeg,
    $"-y -f lavfi -i \"testsrc=duration=3:size=1280x720:rate=30\" -c:v libx264 -pix_fmt yuv420p \"{src}\"")
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

try
{
    var ok = await new FfmpegService().EncodeAsync(settings, gpu, prog, CancellationToken.None);
    Console.WriteLine($"=== 编码结果: {(ok ? "成功" : "失败")} ===");
}
catch (Exception ex)
{
    Console.WriteLine($"=== 编码抛出异常（这就是闪退根因）===");
    Console.WriteLine($"{ex.GetType().FullName}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

return 0;

// ---------- 更新链路冒烟：直接执行 UpdateService 产品代码 ----------
static async Task<int> UpdateSmokeAsync()
{
    IUpdateService update = new UpdateService();
    var appDir = AppContext.BaseDirectory;

    Console.WriteLine("=== 更新链路冒烟 ===");
    Console.Out.Flush();
    Console.WriteLine($"本地内置版本: {update.GetLocalVersion() ?? "(无)"}");
    Console.WriteLine($"本地内置路径: {ConfigService.BundledFfmpegPath} (exists={ConfigService.HasBundledFfmpeg})");

    // 1) 镜像渠道（默认渠道，必须成功）
    Console.WriteLine("\n--- 镜像渠道查询 ---");
    Console.Out.Flush();
    RemoteFfmpegVersion mirror;
    try
    {
        mirror = await update.GetLatestVersionAsync(UpdateChannel.Mirror);
        Console.WriteLine($"最新版本: {mirror.Tag}");
        Console.WriteLine($"下载地址: {mirror.DownloadUrl}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FAIL] 镜像查询失败: {ex.Message}");
        return 1;
    }

    // 2) GitHub 渠道（依赖网络/代理，失败仅警告）
    Console.WriteLine("\n--- GitHub 渠道查询 ---");
    Console.Out.Flush();
    try
    {
        var gh = await update.GetLatestVersionAsync(UpdateChannel.GitHub);
        Console.WriteLine($"最新版本: {gh.Tag}");
        Console.WriteLine($"下载地址: {gh.DownloadUrl}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[WARN] GitHub 查询失败（网络/代理原因可接受）: {ex.Message}");
    }

    // 3) 版本比较
    var local = update.GetLocalVersion();
    Console.WriteLine($"\n版本比较: local={local} vs mirror={mirror.Tag} -> " +
        (UpdateService.CompareVersions(local, mirror.Tag) == 0 ? "已是最新" : "有新版"));

    if (UpdateService.CompareVersions(local, mirror.Tag) == 0)
    {
        Console.WriteLine("本地已是镜像最高版本，跳过下载测试。");
        return 0;
    }

    // 4) 下载并安装（真实整目录替换本地内置 ffmpeg）
    Console.WriteLine("\n--- 下载并安装（整目录替换）---");
    Console.Out.Flush();
    var progress = new Progress<double>(p => Console.Write($"\r下载进度: {p:F0}%   "));
    try
    {
        await update.DownloadAndInstallAsync(mirror.DownloadUrl, mirror.Tag, progress);
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[FAIL] 下载/安装失败: {ex.Message}");
        return 1;
    }

    // 5) 验证替换结果
    var newVer = update.GetLocalVersion();
    var newExe = ConfigService.BundledFfmpegPath;
    Console.WriteLine($"安装后 VERSION 标记: {newVer}");
    Console.WriteLine($"安装后 ffmpeg.exe 存在: {File.Exists(newExe)}");

    var psi = new ProcessStartInfo(newExe, "-version")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        CreateNoWindow = true
    };
    using var proc = Process.Start(psi)!;
    var firstLine = proc.StandardOutput.ReadLine();
    proc.WaitForExit();
    Console.WriteLine($"ffmpeg -version: {firstLine}");

    var ok = newVer == mirror.Tag && proc.ExitCode == 0;
    Console.WriteLine($"=== 更新链路冒烟: {(ok ? "成功" : "失败")} ===");
    Console.Out.Flush();
    return ok ? 0 : 1;
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
