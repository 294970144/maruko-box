# 用 PowerShell 7 编译 Inno Setup 安装包（编码稳健版）
#
# 用法（PowerShell 7）：
#   pwsh -File build.ps1
#   pwsh -File build.ps1 -PayloadDir "D:\tmp\payload" -OutDir "E:\repo\dist"
#
# 坑备忘：
#   1. pwsh 7 的 [Console]::OutputEncoding 默认是系统代码页(gb2312)，会导致 iscc
#      等外部程序的中文输出被错误解码，故先切到 UTF-8。
#   2. .iss 含中文必须带 UTF-8 BOM（见 marukobox.iss 顶部注释）。
#   3. 从 PowerShell 5.1 用 -Command 调用本脚本会因引号丢失而失败，请用 -File。

param(
    [string]$PayloadDir = "C:\mb_payload",
    [string]$OutDir     = "C:\mb_inno_out",
    [string]$IssFile    = (Join-Path $PSScriptRoot "marukobox.iss")
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$iscc = "C:\Users\zhang\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { throw "找不到 iscc.exe: $iscc" }
if (-not (Test-Path $IssFile)) { throw "找不到 .iss: $IssFile" }

"PayloadDir = $PayloadDir"
"OutDir     = $OutDir"
"IssFile    = $IssFile"

& $iscc "/DPayloadDir=$PayloadDir" "/DOutDir=$OutDir" $IssFile *>&1
"EXITCODE=$LASTEXITCODE"
