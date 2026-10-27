using CommunityToolkit.Mvvm.ComponentModel;

using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Automation;

using System.Windows.Media;

namespace DeepVisionSharp.Shell.ViewModels.Dashboard;

/// <summary>
/// 项目面板卡片行。可变 + INPC：刷新与缩略图回写原地更新同一实例，
/// 列表用稳定实例增量更新，避免整表替换导致 ItemsControl 重建容器并重放入场动画。
/// </summary>
public sealed partial class ProjectRow : ObservableObject
{
    private ProjectRow(
        Guid id,
        string name,
        string description,
        string typeKey,
        string typeLabel,
        string sampleCountText,
        int sampleCount,
        string labelCountText,
        int labelCount,
        double progress,
        string progressText,
        string lastUpdatedText,
        string createdAt,
        DateTimeOffset lastUpdatedSortKey,
        string? thumbnailPath,
        ImageSource? thumbnail)
    {
        Id = id;
        Name = name;
        Description = description;
        TypeKey = typeKey;
        TypeLabel = typeLabel;
        SampleCountText = sampleCountText;
        SampleCount = sampleCount;
        LabelCountText = labelCountText;
        LabelCount = labelCount;
        Progress = progress;
        ProgressText = progressText;
        LastUpdatedText = lastUpdatedText;
        CreatedAt = createdAt;
        LastUpdatedSortKey = lastUpdatedSortKey;
        ThumbnailPath = thumbnailPath;
        Thumbnail = thumbnail;
    }

    public Guid Id { get; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _typeKey = string.Empty;

    [ObservableProperty]
    private string _typeLabel = string.Empty;

    [ObservableProperty]
    private string _sampleCountText = string.Empty;

    [ObservableProperty]
    private int _sampleCount;

    [ObservableProperty]
    private string _labelCountText = string.Empty;

    [ObservableProperty]
    private int _labelCount;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _lastUpdatedText = string.Empty;

    /// <summary>创建时间展示文本，创建后不再变化。</summary>
    public string CreatedAt { get; }

    [ObservableProperty]
    private DateTimeOffset _lastUpdatedSortKey;

    [ObservableProperty]
    private string? _thumbnailPath;

    [ObservableProperty]
    private ImageSource? _thumbnail;

    public bool HasThumbnail => Thumbnail is not null;

    public string AutomationId => UiAutomationIds.Dashboard.ProjectCard(Id);

    public string EnterAutomationId => UiAutomationIds.Dashboard.ProjectEnter(Id);

    partial void OnThumbnailChanged(ImageSource? value) => OnPropertyChanged(nameof(HasThumbnail));

    public static ProjectRow CreateFromDto(DashboardProjectDto dto)
        => new(
            dto.Id,
            dto.Name,
            string.IsNullOrWhiteSpace(dto.Description) ? "暂无描述" : dto.Description,
            string.IsNullOrWhiteSpace(dto.Type) ? ProjectTypes.Default : dto.Type,
            ProjectTypes.GetDisplayLabel(dto.Type),
            $"{dto.SampleCount} 样本",
            dto.SampleCount,
            $"{dto.LabelCount} 标签",
            dto.LabelCount,
            dto.Progress,
            dto.Progress.ToString("P0"),
            FormatRelativeTime(dto.LastUpdatedAt),
            dto.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            dto.LastUpdatedAt,
            dto.ThumbnailPath,
            null);

    /// <summary>
    /// 用最新 DTO 原地更新标量字段，保留已加载的缩略图。
    /// 缩略图路径变化时清空旧图，由刷新流程重新解码。
    /// </summary>
    public void ApplyFromDto(DashboardProjectDto dto)
    {
        Name = dto.Name;
        Description = string.IsNullOrWhiteSpace(dto.Description) ? "暂无描述" : dto.Description;
        TypeKey = string.IsNullOrWhiteSpace(dto.Type) ? ProjectTypes.Default : dto.Type;
        TypeLabel = ProjectTypes.GetDisplayLabel(dto.Type);
        SampleCount = dto.SampleCount;
        SampleCountText = $"{dto.SampleCount} 样本";
        LabelCount = dto.LabelCount;
        LabelCountText = $"{dto.LabelCount} 标签";
        Progress = dto.Progress;
        ProgressText = dto.Progress.ToString("P0");
        LastUpdatedSortKey = dto.LastUpdatedAt;
        LastUpdatedText = FormatRelativeTime(dto.LastUpdatedAt);

        if (!string.Equals(ThumbnailPath, dto.ThumbnailPath, StringComparison.OrdinalIgnoreCase))
        {
            ThumbnailPath = dto.ThumbnailPath;
            if (Thumbnail is not null)
            {
                Thumbnail = null;
            }
        }
    }

    public static ProjectRow CreateFromProject(Project project, int sampleCount, int labelCount, double progress)
        => new(
            project.Id,
            project.Name,
            string.IsNullOrWhiteSpace(project.Description) ? "暂无描述" : project.Description,
            string.IsNullOrWhiteSpace(project.Type) ? ProjectTypes.Default : project.Type,
            ProjectTypes.GetDisplayLabel(project.Type),
            $"{sampleCount} 样本",
            sampleCount,
            $"{labelCount} 标签",
            labelCount,
            progress,
            progress.ToString("P0"),
            FormatRelativeTime(project.LastUpdatedAt),
            project.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            project.LastUpdatedAt,
            null,
            null);

    public void ApplyFromProject(Project project, int sampleCount, int labelCount, double progress)
    {
        Name = project.Name;
        Description = string.IsNullOrWhiteSpace(project.Description) ? "暂无描述" : project.Description;
        TypeKey = string.IsNullOrWhiteSpace(project.Type) ? ProjectTypes.Default : project.Type;
        TypeLabel = ProjectTypes.GetDisplayLabel(project.Type);
        SampleCount = sampleCount;
        SampleCountText = $"{sampleCount} 样本";
        LabelCount = labelCount;
        LabelCountText = $"{labelCount} 标签";
        Progress = progress;
        ProgressText = progress.ToString("P0");
        LastUpdatedSortKey = project.LastUpdatedAt;
        LastUpdatedText = FormatRelativeTime(project.LastUpdatedAt);
    }

    private static string FormatRelativeTime(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.Now - timestamp.ToLocalTime();
        if (elapsed.TotalMinutes < 1)
        {
            return "刚刚更新";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前";
        }

        if (elapsed.TotalDays < 7)
        {
            return $"{Math.Max(1, (int)elapsed.TotalDays)} 天前";
        }

        return timestamp.ToLocalTime().ToString("yyyy-MM-dd");
    }
}

public sealed record ProjectTypeFilterOption(string Value, string Label);
