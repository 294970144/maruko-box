using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MarukoBox.Helpers;

/// <summary>
/// 危险操作二次确认（P0-D 统一入口）：清空队列 / 清空轨道等不可撤销操作，
/// 弹 ContentDialog 要求确认后才执行。任何失败按「取消」处理（宁可多点一次）。
/// </summary>
public static class UiConfirm
{
    /// <summary>
    /// 弹出确认对话框。返回 true 表示用户点击了确认按钮。
    /// </summary>
    /// <param name="title">对话框标题（如「清空队列」）。</param>
    /// <param name="message">说明将发生什么、为什么不可撤销。</param>
    /// <param name="confirmText">确认按钮文字（默认「确认」；危险动词如「清空」更直观）。</param>
    public static async Task<bool> ShowAsync(string title, string message, string confirmText = "确认")
    {
        try
        {
            var xamlRoot = App.Window?.Content?.XamlRoot;
            if (xamlRoot is null)
            {
                return false;
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = confirmText,
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
        catch
        {
            // 对话框弹不出来（如窗口正在关闭）一律视为取消
            return false;
        }
    }
}
