using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MarukoBox.Services;

namespace MarukoBox.Models;

/// <summary>
/// 「程序员」专列 ffmpeg 版本列表的 UI 行模型。
/// 由 SettingsViewModel 在拉取 GitHub 全部 release 后构建；用于绑定到 ListView。
/// </summary>
public sealed partial class FfmpegReleaseRow : ObservableObject
{
    /// <summary>jellyfin-ffmpeg tag（如 "8.1.2-3"）。</summary>
    public string Tag { get; }

    /// <summary>win64 便携版 zip 的 GitHub 资产 URL。</summary>
    public string AssetUrl { get; }

    /// <summary>UI 显示用的发布日期（"2026-09-05"）。</summary>
    public string PublishedAtText { get; }

    /// <summary>UI 显示用的资产大小（"67.2 MB"）。</summary>
    public string SizeText { get; }

    /// <summary>是否为 GitHub 标 prerelease（8.x 系列当前为 prerelease）。</summary>
    public bool IsPrerelease { get; }

    /// <summary>
    /// 是否通过本机驱动兼容性门槛（8.x 需驱动 ≥610）。
    /// 程序员用户被允许跳过此限制强制安装——因此 IsCompatible=false 时按钮仍可点。
    /// </summary>
    public bool IsCompatible { get; }

    /// <summary>兼容性状态的中文文本（"✓ 兼容本机" / "⚠ 驱动过旧，可强制安装"）。</summary>
    public string CompatibleText { get; }

    /// <summary>行内 [安装此版本] 按钮的命令（由 SettingsViewModel 注入，绕过驱动门槛强装用）。</summary>
    public ICommand InstallCommand { get; }

    /// <summary>下载/安装进行中状态（控制按钮禁用与进度环显示）。</summary>
    [ObservableProperty]
    public partial bool IsInstalling { get; set; }

    /// <summary>下载进度百分比（0–100）。</summary>
    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    public FfmpegReleaseRow(RemoteFfmpegRelease src, FfmpegUpdateOffer offer, ICommand installCommand)
    {
        Tag = src.Tag;
        AssetUrl = src.AssetUrl;
        PublishedAtText = src.PublishedAt == DateTimeOffset.UnixEpoch
            ? "日期未知"
            : src.PublishedAt.LocalDateTime.ToString("yyyy-MM-dd");
        SizeText = FormatSize(src.AssetSizeBytes);
        IsPrerelease = src.IsPrerelease;
        IsCompatible = offer.Offer;
        CompatibleText = offer.Offer
            ? "✓ 兼容本机"
            : $"⚠ {offer.BlockReason ?? "驱动兼容性未知"}，可强制安装";
        InstallCommand = installCommand;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "大小未知";
        const double kb = 1024d;
        const double mb = kb * 1024d;
        if (bytes < mb) return $"{bytes / kb:F1} KB";
        return $"{bytes / mb:F1} MB";
    }
}
