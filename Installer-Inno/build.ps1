# 用 PowerShell 7 编译 Inno Setup 安装包（编码稳健版）
#
# 用法（PowerShell 7）：
#   pwsh -File build.ps1
#   pwsh -File build.ps1 -PayloadDir "D:\tmp\payload" -OutDir "E:\repo\dist"
#   pwsh -File build.ps1 -IsccPath "D:\tools\ISCC.exe"     # 显式指定编译器
#   $env:ISCC_PATH = "D:\tools\ISCC.exe"; pwsh -File build.ps1
#
# 坑备忘：
#   1. pwsh 7 的 [Console]::OutputEncoding 默认是系统代码页(gb2312)，会导致 iscc
#      等外部程序的中文输出被错误解码，故先切到 UTF-8。
#   2. .iss 含中文必须带 UTF-8 BOM（见 marukobox.iss 顶部注释）。
#   3. 从 PowerShell 5.1 用 -Command 调用本脚本会因引号丢失而失败，请用 -File。
#   4. 退出码必须显式 exit（见文件末尾），否则调用方读到的是别的数字。

param(
    [string]$PayloadDir = "C:\mb_payload",
    [string]$OutDir     = "C:\mb_inno_out",
    [string]$IssFile    = (Join-Path $PSScriptRoot "marukobox.iss"),
    [string]$IsccPath   = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# ---------- 定位 ISCC.exe ----------
# 【M6 修复】此前硬编码 C:\Users\<某人>\AppData\Local\Programs\Inno Setup 6\ISCC.exe，
# 换机器或换协作者必挂。改为按「显式参数 → 环境变量 → PATH → 注册表 InstallLocation → 常见目录」
# 依次探测。注册表这一路是实测确认的：Inno Setup 自身的卸载项形如
#   HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1
# 其中的 InstallLocation 即安装目录（本机实测：C:\Users\zhang\AppData\Local\Programs\Inno Setup 6\）。
function Find-Iscc {
    param([string]$Explicit)

    if (-not [string]::IsNullOrWhiteSpace($Explicit) -and (Test-Path -LiteralPath $Explicit)) {
        return $Explicit
    }

    $fromEnv = $env:ISCC_PATH
    if (-not [string]::IsNullOrWhiteSpace($fromEnv) -and (Test-Path -LiteralPath $fromEnv)) {
        return $fromEnv
    }

    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $roots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
    )

    foreach ($root in $roots) {
        $keys = Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue |
                Where-Object { $_.PSChildName -like 'Inno Setup*' }

        foreach ($k in $keys) {
            $loc = (Get-ItemProperty -LiteralPath $k.PSPath -ErrorAction SilentlyContinue).InstallLocation
            if ([string]::IsNullOrWhiteSpace($loc)) { continue }

            $cand = Join-Path $loc 'ISCC.exe'
            if (Test-Path -LiteralPath $cand) { return $cand }
        }
    }

    foreach ($cand in @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )) {
        if (Test-Path -LiteralPath $cand) { return $cand }
    }

    return $null
}

$iscc = Find-Iscc -Explicit $IsccPath
if (-not $iscc) {
    throw "找不到 iscc.exe。请用 -IsccPath 指定、设置环境变量 ISCC_PATH，或把 Inno Setup 加进 PATH。"
}
if (-not (Test-Path $IssFile)) { throw "找不到 .iss: $IssFile" }

"PayloadDir = $PayloadDir"
"OutDir     = $OutDir"
"IssFile    = $IssFile"
"Iscc       = $iscc"

& $iscc "/DPayloadDir=$PayloadDir" "/DOutDir=$OutDir" $IssFile *>&1
"EXITCODE=$LASTEXITCODE"

# 【S2 修复】必须把 iscc 的退出码传出去。
# 此前只打印不退出：外层 build-installer.ps1 用 $LASTEXITCODE 判断成败时，
# 读到的是「本次脚本自身」的退出码（0），iscc 编译失败也会被当成成功，
# dist 里没有新安装包却照样打印「=== 完成 ===」。
exit $LASTEXITCODE
