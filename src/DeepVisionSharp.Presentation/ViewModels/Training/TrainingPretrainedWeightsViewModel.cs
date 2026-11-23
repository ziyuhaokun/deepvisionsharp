using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Presentation.Services;

namespace DeepVisionSharp.Presentation.ViewModels.Training;

/// <summary>
/// 训练配置里的「预训练权重」一行：告诉用户当前模型有没有预训练权重，没有就能直接下载。
/// Owned by <see cref="TrainingConfigViewModel"/>; the model type is pushed via <see cref="SetModelType"/>.
/// </summary>
public sealed partial class TrainingPretrainedWeightsViewModel : ObservableObject
{
    private readonly IPretrainedWeightsService _service;
    private readonly ShellSnackbarNotifier _snackbar;
    private string _modelType = string.Empty;
    private PretrainedWeightsStatus _status = new(false, false, string.Empty, 0);
    private CancellationTokenSource? _downloadCts;
    private string _downloadingDisplayName = string.Empty;

    public TrainingPretrainedWeightsViewModel(IPretrainedWeightsService service, ShellSnackbarNotifier snackbar)
    {
        _service = service;
        _snackbar = snackbar;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    private bool _isSupported;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    [NotifyPropertyChangedFor(nameof(IsMissing))]
    private bool _isAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    private bool _isDownloading;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>支持下载但本机没有：用警告样式提示。</summary>
    public bool IsMissing => IsSupported && !IsAvailable;

    public bool ShowDownloadButton => IsSupported && (!IsAvailable || _status.UsesFallback) && !IsDownloading;

    public void SetModelType(string modelType)
    {
        _modelType = modelType;
        Refresh();
    }

    private void Refresh()
    {
        _status = _service.GetStatus(_modelType);
        IsSupported = _status.Supported;
        IsAvailable = _status.Available;
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(ShowDownloadButton));
        StatusText = BuildStatusText(_status);
    }

    private string BuildStatusText(PretrainedWeightsStatus status)
    {
        if (!status.Supported)
        {
            return string.Empty;
        }

        if (IsDownloading)
        {
            return $"正在准备 {_downloadingDisplayName} 预训练权重… {ProgressPercent:0}%";
        }

        if (status.UsesFallback)
        {
            return $"已就绪：可使用同系列分类权重初始化骨干。可下载 {status.DisplayName} 专用权重（约 {status.ApproxMegabytes} MB）。";
        }

        return status.Available
            ? $"已就绪：{status.DisplayName} 的预训练权重，训练会自动使用。"
            : $"未找到 {status.DisplayName} 的预训练权重，将从随机初始化开始训练；样本不多时效果通常很差。"
              + $"建议先下载（约 {status.ApproxMegabytes} MB）。";
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (!ShowDownloadButton)
        {
            return;
        }

        var modelType = _modelType;
        using var cancellation = new CancellationTokenSource();
        _downloadCts = cancellation;
        _downloadingDisplayName = _status.DisplayName;
        IsDownloading = true;
        ProgressPercent = 0;
        Refresh();

        // Progress<T> captures the UI synchronization context created here.
        var progress = new Progress<double>(value =>
        {
            var percent = Math.Clamp(value, 0, 1) * 100;
            if (!ReferenceEquals(_downloadCts, cancellation) || percent <= ProgressPercent)
            {
                return;
            }

            ProgressPercent = percent;
            StatusText = BuildStatusText(_status);
        });

        try
        {
            await _service.DownloadAsync(modelType, progress, cancellation.Token);
            _snackbar.ShowSuccess("预训练权重已下载，开始训练时会自动使用。", "训练");
        }
        catch (OperationCanceledException)
        {
            _snackbar.ShowWarning("已取消下载。", "训练");
        }
        catch (Exception ex)
        {
            _snackbar.ShowError($"下载预训练权重失败：{ex.Message}", "训练");
        }
        finally
        {
            _downloadCts = null;
            IsDownloading = false;
            Refresh();
        }
    }

    [RelayCommand]
    private void CancelDownload() => _downloadCts?.Cancel();
}
