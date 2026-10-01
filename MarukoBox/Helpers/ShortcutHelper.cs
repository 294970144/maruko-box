using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace MarukoBox.Helpers;

/// <summary>
/// 为 unpackaged（非 MSIX）应用创建 / 修复「带 System.AppUserModel.ID 的开始菜单快捷方式」。
/// <para>
/// Windows App SDK 的 <c>AppNotificationManager</c> 在 unpackaged 模式下注册通知通道时，
/// 要求存在一个指向当前 exe、且在属性存储里设置了 <c>System.AppUserModel.ID</c> 的开始菜单快捷方式，
/// 否则 <c>Register()</c> 会抛 COMException（AUMID 未注册），导致所有系统通知永远弹不出来。
/// </para>
/// <para>
/// 本类移植自 Windows App SDK 官方 unpackaged 通知示例的 ShortcutHelper：
/// 首次运行（开发 / 全新安装）创建该快捷方式；已存在（Inno Setup 安装的「MarukoBox 2026.lnk」）
/// 则载入并补设 AUMID，避免重复的开始菜单入口、也不破坏 Inno 的卸载登记。
/// </para>
/// </summary>
internal static class ShortcutHelper
{
    // System.AppUserModel.ID 的属性键：{9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, pid 5
    private static readonly Guid PKEY_AppUserModelId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private const int PKEY_AppUserModelIdPid = 5;
    private const int STGM_READWRITE = 2;
    private const ushort VT_LPWSTR = 31;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, out IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out int cProps);
        void GetAt(int iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr pwszVal;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile(out IntPtr ppszFileName);
    }

    /// <summary>
    /// 确保开始菜单存在指向当前 exe 且带有指定 AUMID 的快捷方式。
    /// 任意异常（COM 不可用 / 无写入权限）向上抛，由调用方 try/catch 降级。
    /// </summary>
    /// <param name="aumId">应用用户模型 ID（unpackaged 下为自定义字符串，需与注册时一致）。</param>
    /// <param name="displayName">快捷方式显示名（与 Inno Setup 的「MarukoBox 2026」保持一致，避免重复入口）。</param>
    public static void EnsureShortcut(string aumId, string displayName)
    {
        string programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrEmpty(programsPath))
        {
            throw new InvalidOperationException("无法解析开始菜单 Programs 目录");
        }

        string shortcutPath = Path.Combine(programsPath, displayName + ".lnk");

        IShellLinkW shellLink = (IShellLinkW)new ShellLink();

        if (File.Exists(shortcutPath))
        {
            // 已存在（多为 Inno Setup 安装的入口）：载入后仅补设 AUMID，保留图标等原有属性。
            ((IPersistFile)shellLink).Load(shortcutPath, STGM_READWRITE);
        }
        else
        {
            var mainModule = Process.GetCurrentProcess().MainModule;
            if (mainModule == null)
            {
                throw new InvalidOperationException("无法获取当前进程主模块路径");
            }

            string exePath = mainModule.FileName;
            string? exeDir = Path.GetDirectoryName(exePath);

            shellLink.SetPath(exePath);
            shellLink.SetDescription(displayName);
            shellLink.SetWorkingDirectory(exeDir ?? string.Empty);
            shellLink.SetIconLocation(Path.Combine(exeDir ?? string.Empty, "Assets", "AppIcon.ico"), 0);
        }

        IPropertyStore propertyStore = (IPropertyStore)shellLink;
        PropertyKey key = new PropertyKey { fmtid = PKEY_AppUserModelId, pid = PKEY_AppUserModelIdPid };

        PropVariant value = new PropVariant
        {
            vt = VT_LPWSTR,
            pwszVal = Marshal.StringToCoTaskMemUni(aumId)
        };
        propertyStore.SetValue(ref key, ref value);
        propertyStore.Commit();
        Marshal.FreeCoTaskMem(value.pwszVal);

        ((IPersistFile)shellLink).Save(shortcutPath, true);
    }
}
