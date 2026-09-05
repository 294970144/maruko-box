# 一键构建安装包：dotnet publish(unpackaged) -> 捆绑内置 ffmpeg -> iscc 编译 -> 输出到 dist
#
# 用法（PowerShell 7）：
#   pwsh -File build-installer.ps1                 # 默认 payload 落到 $env:TEMP，构建完自动清理
#   pwsh -File build-installer.ps1 -KeepPayload    # 保留 payload 目录便于排查
#   pwsh -File build-installer.ps1 -FfmpegZip "D:\path\jellyfin-ffmpeg_7.1.1-5-portable_win64.zip"
#
# 内置 ffmpeg 的来源（优先级：-FfmpegZip 参数 > third_party\jellyfin-ffmpeg*.zip > 不捆绑）：
#   仓库不入库二进制，把 jellyfin-ffmpeg 便携版 zip 放到仓库根的 third_party\ 下（已 gitignore），
#   构建脚本会自动发现并解压进 payload 的 ffmpeg\ 子目录，Inno [Files] 递归拷贝装到 {app}\ffmpeg\。
#   不放 zip 也能构建，只是安装包不含内置 ffmpeg（应用回落到 PATH / 手动路径）。

param(
    [string]$RepoRoot    = (Split-Path -Parent $PSScriptRoot),
    [string]$PayloadDir  = (Join-Path $env:TEMP "mb_payload_build"),
    [string]$FfmpegZip   = "",
    [switch]$KeepPayload
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

$csproj = Join-Path $RepoRoot "MarukoBox\MarukoBox.csproj"
$outDir = Join-Path $RepoRoot "dist"

if (-not (Test-Path $csproj)) { throw "找不到项目文件: $csproj" }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# ---------- 解析内置 ffmpeg zip：参数 > third_party 自动发现 ----------
if ([string]::IsNullOrWhiteSpace($FfmpegZip)) {
    $thirdParty = Join-Path $RepoRoot "third_party"
    if (Test-Path $thirdParty) {
        $found = Get-ChildItem -LiteralPath $thirdParty -Filter "jellyfin-ffmpeg*portable_win64.zip" -File |
                 Sort-Object Name -Descending | Select-Object -First 1
        if ($found) { $FfmpegZip = $found.FullName }
    }
}

$bundleFfmpeg = -not [string]::IsNullOrWhiteSpace($FfmpegZip)
if ($bundleFfmpeg -and -not (Test-Path -LiteralPath $FfmpegZip)) {
    throw "找不到指定的 ffmpeg zip: $FfmpegZip"
}

"=== 1/4  dotnet publish (unpackaged) -> $PayloadDir ==="
& dotnet publish $csproj -c Release `
    -p:WindowsPackageType=None -p:EnableMsixTooling=false `
    -p:WindowsAppSDKSelfContained=true --self-contained true -r win-x64 `
    -p:PublishTrimmed=false -o $PayloadDir *>&1
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE" }

if ($bundleFfmpeg) {
    "=== 2/4  捆绑内置 ffmpeg <- $FfmpegZip ==="
    $ffDir = Join-Path $PayloadDir "ffmpeg"
    New-Item -ItemType Directory -Force -Path $ffDir | Out-Null
    Expand-Archive -LiteralPath $FfmpegZip -DestinationPath $ffDir -Force

    # 从 zip 文件名解析版本号写入 VERSION 标记（应用「检查更新」据此判断是否新版）
    # 文件名形如 jellyfin-ffmpeg_7.1.1-5-portable_win64.zip -> 7.1.1-5
    $versionTag = "unknown"
    if ($FfmpegZip -match 'jellyfin-ffmpeg_(\d+(?:\.\d+)+-\d+)-portable') {
        $versionTag = $Matches[1]
    }
    Set-Content -LiteralPath (Join-Path $ffDir "VERSION") -Value $versionTag -Encoding ascii
    "    内置 ffmpeg 版本: $versionTag（payload\ffmpeg\）"
} else {
    "=== 2/4  跳过内置 ffmpeg（未提供 jellyfin-ffmpeg zip）==="
}

"=== 3/4  iscc 编译安装包 -> $outDir ==="
& (Join-Path $PSScriptRoot "build.ps1") -PayloadDir $PayloadDir -OutDir $outDir
if ($LASTEXITCODE -ne 0) { throw "iscc 编译失败，退出码 $LASTEXITCODE" }

"=== 4/4  清理中间 payload ==="
if ($KeepPayload) {
    "已保留 payload: $PayloadDir"
} else {
    Remove-Item -LiteralPath $PayloadDir -Recurse -Force -ErrorAction SilentlyContinue
    "已清理 payload: $PayloadDir"
}

"=== 完成，产物在 $outDir ==="
