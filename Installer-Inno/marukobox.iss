; 小丸工具箱 2026 —— Inno Setup 安装包（零 UAC / 当前用户 / 中文向导）
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

#define MyAppName "小丸工具箱 2026"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "MarukoBox"
#define MyAppURL "https://github.com/"
#define MyAppExeName "MarukoBox.exe"

[Setup]
AppId={{9F3E1C2A-4B7D-4E5F-8C2A-1B3D4E5F6A7B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
; 当前用户安装，不触发 UAC
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
DefaultDirName={localappdata}\Programs\MarukoBox
DefaultGroupName={#MyAppName}
OutputDir={#OutDir}
OutputBaseFilename=MarukoBoxSetup-Inno_{#MyAppVersion}
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

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
;
