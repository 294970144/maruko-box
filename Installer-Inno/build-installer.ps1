# 一键构建安装包：dotnet publish(unpackaged) -> iscc 编译 -> 输出到 dist
#
# 用法（PowerShell 7）：
#   pwsh -File build-installer.ps1                 # 默认 payload 落到 $env:TEMP，构建完自动清理
#   pwsh -File build-installer.ps1 -KeepPayload    # 保留 payload 目录便于排查
#
# 说明：payload 只是中间产物（可由源码重新 publish 生成），默认输出到 Temp 并在成功后删除，
#       因此 C:\mb_payload 这类固定目录不再是重建安装包的必需依赖。

param(
    [string]$RepoRoot    = (Split-Path -Parent $PSScriptRoot),
    [string]$PayloadDir  = (Join-Path $env:TEMP "mb_payload_build"),
    [switch]$KeepPayload
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

$csproj = Join-Path $RepoRoot "MarukoBox\MarukoBox.csproj"
$outDir = Join-Path $RepoRoot "dist"

if (-not (Test-Path $csproj)) { throw "找不到项目文件: $csproj" }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

"=== 1/3  dotnet publish (unpackaged) -> $PayloadDir ==="
& dotnet publish $csproj -c Release `
    -p:WindowsPackageType=None -p:EnableMsixTooling=false `
    -p:WindowsAppSDKSelfContained=true --self-contained true -r win-x64 `
    -p:PublishTrimmed=false -o $PayloadDir *>&1
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE" }

"=== 2/3  iscc 编译安装包 -> $outDir ==="
& (Join-Path $PSScriptRoot "build.ps1") -PayloadDir $PayloadDir -OutDir $outDir
if ($LASTEXITCODE -ne 0) { throw "iscc 编译失败，退出码 $LASTEXITCODE" }

"=== 3/3  清理中间 payload ==="
if ($KeepPayload) {
    "已保留 payload: $PayloadDir"
} else {
    Remove-Item -LiteralPath $PayloadDir -Recurse -Force -ErrorAction SilentlyContinue
    "已清理 payload: $PayloadDir"
}

"=== 完成，产物在 $outDir ==="
