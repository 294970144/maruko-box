using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MarukoBox.Helpers;

/// <summary>
/// 文件夹选择器的统一封装：自动用主窗口 HWND 初始化 WinRT picker，
/// 供各页「输出文件夹」浏览按钮复用，避免每处重复 WinRT 初始化代码。
/// </summary>
public static class PickerHelper
{
    /// <summary>弹出文件夹选择器，返回所选路径；用户取消则返回 null。</summary>
    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary
        };
        picker.FileTypeFilter.Add("*");

        InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
