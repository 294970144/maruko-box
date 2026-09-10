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
#define MyAppVersion "1.5.0"
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
; 覆盖安装时若应用正在运行：经重启管理器静默关闭后替换文件，
; 避免「文件被占用 → 中止安装」。只影响使用被替换文件的进程（即本应用）。
CloseApplications=force

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

; ---------------------------------------------------------------------------
; 旧版本检测与自动卸载（ssInstall：即将复制新文件之前）
;
; 背景：AppId 不变时 Inno 本身就是「覆盖式升级」——同一目录、共用同一卸载日志、
; 不会产生重复的「添加/删除程序」条目（官方 FAQ：How to create an installation
; that is an "update" or "add-on"）。这里在此之上再做一次静默卸载旧版，用于：
;   1) 清掉新包中已移除的旧文件（纯覆盖安装会一直残留）；
;   2) 旧 exe 被删除后，资源管理器/任务栏的图标缓存自然失效，新图标即时可见。
;
; 用户数据不受影响：config.json / session.json 存放在
; %LOCALAPPDATA%\MarukoBox（{app} 之外），且 [UninstallDelete] 只删 {app}。
; ---------------------------------------------------------------------------
[Code]
const
  // 与 [Setup] AppId 一一对应：Inno 的卸载注册表键固定为 <AppId>_is1。
  // 本安装包 PrivilegesRequired=lowest（当前用户安装），键在 HKCU；
  // 仍先查 HKLM 兜底历史遗留的管理员安装。
  UninstRegKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9F3E1C2A-4B7D-4E5F-8C2A-1B3D4E5F6A7B}_is1';

function GetOldUninstallString(): String;
var
  S: String;
begin
  Result := '';

  if not RegQueryStringValue(HKCU, UninstRegKey, 'UninstallString', S) then
  begin
    if not RegQueryStringValue(HKLM, UninstRegKey, 'UninstallString', S) then
    begin
      Exit; // 没有已安装的旧版本
    end;
  end;

  // UninstallString 一般是带引号的 "...\unins000.exe"
  Result := RemoveQuotes(Trim(S));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  OldUninstaller: String;
  ResultCode: Integer;
begin
  if CurStep <> ssInstall then
  begin
    Exit;
  end;

  OldUninstaller := GetOldUninstallString();
  if (OldUninstaller <> '') and FileExists(OldUninstaller) then
  begin
    // 静默卸载旧版：不弹确认框、不重启，等卸载器退出后再继续复制新文件。
    // 旧版若正在运行，其 exe 可能被占用而延后删除——
    // [Setup] 的 CloseApplications=force 会在随后替换文件前经重启管理器关闭它。
    Exec(OldUninstaller, '/SILENT /SUPPRESSMSGBOXES /NORESTART', '',
         SW_SHOW, ewWaitUntilTerminated, ResultCode);
  end;
end;

