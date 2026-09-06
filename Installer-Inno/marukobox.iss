; MarukoBox 2026 —— Inno Setup 安装包（零 UAC / 当前用户 / 中文向导）
; 注意：本文件含中文，必须是 UTF-8 BOM，否则 iscc 读中文会乱码。
;       改过本文件后若去掉了 BOM，请用 Python 补回：
;       open(p,'wb').write(b'\xef\xbb\xbf'+open(p,'rb').read())
;
; 打包源与输出目录均可用 iscc 的 /D 参数覆盖（脚本默认回落到下面的常量）：
;   iscc /DPayloadDir="D:\tmp\payload" /DOutDir="E:\repo\dist" marukobox.iss

#ifndef PayloadDir
  #define PayloadDir "C:\mb_payload"
#endif
#ifndef OutDir
  #define OutDir "C:\mb_inno_out"
#endif

#define MyAppName "MarukoBox 2026"
#define MyAppVersion "1.4.1"
#define MyAppPublisher "MarukoBox"
#define MyAppURL "https://github.com/"
#define MyAppExeName "MarukoBox.exe"

[Setup]
AppId={{9F3E1C2A-4B7D-4E5F-8C2A-1B3D4E5F6A7B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
; 文件属性里的「文件版本」：默认 0.0.0.0，必须显式指定（产品版本取 AppVersion）
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
; 安装向导内展示的许可证（仓库根 LICENSE，GPL-3.0；内置 ffmpeg 为 GPL，见 THIRD-PARTY-NOTICES）
LicenseFile=..\LICENSE
; 当前用户安装，不触发 UAC
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
DefaultDirName={localappdata}\Programs\MarukoBox
DefaultGroupName={#MyAppName}
OutputDir={#OutDir}
OutputBaseFilename=MarukoBoxSetup-Inno_{#MyAppVersion}
; 安装包自身的图标（资源管理器里显示）
SetupIconFile={#PayloadDir}\Assets\AppIcon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; 64 位模式（x64compatible 取代已废弃的 x64，避免编译警告）
ArchitecturesInstallIn64BitMode=x64compatible
ChangesAssociations=no

[Languages]
; 官方 ChineseSimplified.isl 需自行放入 Inno 的 Languages 目录
; （winget 装的 6.7.3 精简版语言包未带中文）
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; 卸载后清掉整个安装目录：WindowsAppSDK 的语言资源子目录（af-ZA 等 25 个）
; 多为空目录，Inno 默认不删，会残留约 60MB 空目录树
[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Icons]
; 显式指定图标文件，使开始菜单/桌面快捷方式显示 AppIcon（而非 exe 默认空白图标）
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
;
