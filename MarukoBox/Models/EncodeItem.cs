using System.ComponentModel;

namespace MarukoBox.Models;

/// <summary>
/// 批量编码队列中的单个任务项。
/// 绑定到视频页左侧队列列表。
/// </summary>
public class EncodeItem : INotifyPropertyChanged
{
    private string _inputPath = string.Empty;
    private string _statusText = "等待中";
    private double _percent;
    private bool _isEncoding;
    private bool _isDone;
    private bool _hasError;

    /// <summary>输入文件完整路径。</summary>
    public string InputPath
    {
        get => _inputPath;
        set
        {
            if (_inputPath != value)
            {
                _inputPath = value;
                OnPropertyChanged(nameof(InputPath));
                OnPropertyChanged(nameof(FileName));
            }
        }
    }

    /// <summary>仅文件名（用于列表展示）。</summary>
    public string FileName => string.IsNullOrEmpty(_inputPath)
        ? string.Empty
        : System.IO.Path.GetFileName(_inputPath);

    /// <summary>状态文本。</summary>
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    /// <summary>当前进度百分比 0~100。</summary>
    public double Percent
    {
        get => _percent;
        set
        {
            if (_percent != value)
            {
                _percent = value;
                OnPropertyChanged(nameof(Percent));
            }
        }
    }

    /// <summary>是否正在编码。</summary>
    public bool IsEncoding
    {
        get => _isEncoding;
        set
        {
            if (_isEncoding != value)
            {
                _isEncoding = value;
                OnPropertyChanged(nameof(IsEncoding));
            }
        }
    }

    /// <summary>是否已完成。</summary>
    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (_isDone != value)
            {
                _isDone = value;
                OnPropertyChanged(nameof(IsDone));
            }
        }
    }

    /// <summary>是否出错。</summary>
    public bool HasError
    {
        get => _hasError;
        set
        {
            if (_hasError != value)
            {
                _hasError = value;
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
