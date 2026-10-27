using CommunityToolkit.Mvvm.ComponentModel;

using DeepVisionSharp.Shell.Services;

namespace DeepVisionSharp.Shell.ViewModels.Training;

/// <summary>
/// Host CPU/GPU/memory resource gauges for the training run panel.
/// Owned by <see cref="TrainingMonitorViewModel"/>.
/// </summary>
public sealed partial class TrainingResourcePanelViewModel : ObservableObject
{
    private readonly TrainingResourcePoller _resourcePolling;
    private readonly IUiDispatcher _uiDispatcher;
    private Func<bool>? _isResourcesSectionActive;

    public TrainingResourcePanelViewModel(
        TrainingResourcePoller resourcePolling,
        IUiDispatcher uiDispatcher)
    {
        _resourcePolling = resourcePolling;
        _uiDispatcher = uiDispatcher;
    }

    [ObservableProperty]
    private double _resourceCpuPercent;

    [ObservableProperty]
    private double _resourceMemoryPercent;

    [ObservableProperty]
    private double _resourceGpuPercent;

    [ObservableProperty]
    private bool _hasResourceData;

    [ObservableProperty]
    private bool _hasGpuResourceData;

    [ObservableProperty]
    private string? _resourcePollingError;

    public bool HasResourcePollingError => !string.IsNullOrWhiteSpace(ResourcePollingError);

    public void AttachSectionGate(Func<bool> isResourcesSectionActive) =>
        _isResourcesSectionActive = isResourcesSectionActive;

    public void StartPolling()
    {
        ResourcePollingError = null;

        _resourcePolling.Start(
            () => _isResourcesSectionActive?.Invoke() ?? false,
            (cpu, memory, gpu, hasGpu) =>
            {
                ResourceCpuPercent = cpu;
                ResourceMemoryPercent = memory;
                ResourceGpuPercent = gpu;
                HasGpuResourceData = hasGpu;
                HasResourceData = true;
            },
            ReportPollingError);
    }

    public void StopPolling() => _resourcePolling.Stop();

    partial void OnResourcePollingErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasResourcePollingError));

    private void ReportPollingError(Exception ex)
    {
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            ResourcePollingError = $"资源轮询失败：{ex.Message}";
        });
    }
}
