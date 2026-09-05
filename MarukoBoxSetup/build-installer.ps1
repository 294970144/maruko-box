# 一键构建 AOT 自解压安装包：publish 主程序 -> 压 zip -> publish AOT stub -> 追加 payload -> dist
#
# 用法（PowerShell 7）：
#   pwsh -File build-installer.ps1                 # 中间产物落 %TEMP%，成功后自动清理
#   pwsh -File build-installer.ps1 -KeepPayload    # 保留中间产物便于排查
#
# 前置条件：
#   1. VS「使用 C++ 的桌面开发」工作负载（AOT 需要 link.exe，验证见 vswhere）
#   2. python 在 PATH 中（append_payload.py 仅用标准库）
#
# 坑备忘：
#   - pwsh 7 的 [Console]::OutputEncoding 默认 gb2312，先切 UTF-8，否则外部程序中文输出乱码
#   - 从 PowerShell 5.1 用 -Command 调本脚本会丢引号，必须 -File
#   - zip 必须把文件放在 zip 根（ZipFile.CreateFromDirectory），安装器按 ExtractToDirectory 解

param(
    [string]$RepoRoot   = (Split-Path -Parent $PSScriptRoot),
    [string]$PayloadDir = (Join-Path $env:TEMP "mb_payload_aot"),
    [string]$StubDir    = (Join-Path $env:TEMP "mb_stub_aot"),
    [switch]$KeepPayload
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

$appCsproj = Join-Path $RepoRoot "MarukoBox\MarukoBox.csproj"
$setupCsproj = Join-Path $PSScriptRoot "MarukoBoxSetup.csproj"
$appendPy = Join-Path $PSScriptRoot "append_payload.py"
$outDir = Join-Path $RepoRoot "dist"
$outExe = Join-Path $outDir "MarukoBoxSetup_1.0.0.exe"
$zipPath = Join-Path $env:TEMP "mb_payload_aot.zip"

foreach ($f in @($appCsproj, $setupCsproj, $appendPy)) {
    if (-not (Test-Path $f)) { throw "找不到文件: $f" }
}
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

"=== 1/5  dotnet publish 主程序 (unpackaged) -> $PayloadDir ==="
& dotnet publish $appCsproj -c Release `
    -p:WindowsPackageType=None -p:EnableMsixTooling=false `
    -p:WindowsAppSDKSelfContained=true --self-contained true -r win-x64 `
    -p:PublishTrimmed=false -o $PayloadDir *>&1
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE" }

"=== 2/5  压缩 payload -> $zipPath ==="
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($PayloadDir, $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)

"=== 3/5  dotnet publish 安装器 (Native AOT stub) -> $StubDir ==="
& dotnet publish $setupCsproj -c Release -o $StubDir *>&1
if ($LASTEXITCODE -ne 0) { throw "AOT publish 失败（检查 C++ 工具链），退出码 $LASTEXITCODE" }
$stubExe = Join-Path $StubDir "MarukoBoxSetup.exe"
if (-not (Test-Path $stubExe)) { throw "未生成 stub: $stubExe" }

"=== 4/5  追加 payload -> $outExe ==="
& python $appendPy $stubExe $zipPath $outExe
if ($LASTEXITCODE -ne 0) { throw "append_payload 失败，退出码 $LASTEXITCODE" }

"=== 5/5  清理中间产物 ==="
if ($KeepPayload) {
    "已保留: $PayloadDir / $StubDir / $zipPath"
} else {
    Remove-Item -LiteralPath $PayloadDir, $StubDir, $zipPath -Recurse -Force -ErrorAction SilentlyContinue
    "已清理中间产物"
}

"=== 完成：$outExe ==="
