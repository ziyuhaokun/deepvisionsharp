using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Automation;

namespace DeepVisionSharp.Shell.ViewModels.Gallery;

public sealed partial class GalleryRow : ObservableObject
{
    public GalleryRow(
        Guid id,
        Guid projectId,
        string name,
        string description,
        int sampleCount,
        int annotatedCount,
        double progress,
        DateTimeOffset lastUpdatedAt,
        string lastUpdatedText,
        string createdStatus,
        string? thumbnailPath = null,
        ImageSource? thumbnail = null)
    {
        Id = id;
        ProjectId = projectId;
        _name = name;
        _description = description;
        _sampleCount = sampleCount;
        _sampleCountText = $"{sampleCount} 样本";
        _annotatedCount = annotatedCount;
        _annotatedCountText = $"{annotatedCount} 已标注";
        _progress = progress;
        _progressText = progress.ToString("P0");
        _lastUpdatedAt = lastUpdatedAt;
        _lastUpdatedText = lastUpdatedText;
        CreatedStatus = createdStatus;
        _thumbnailPath = thumbnailPath;
        _thumbnail = thumbnail;
    }

    public Guid Id { get; }

    public Guid ProjectId { get; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private int _sampleCount;

    [ObservableProperty]
    private string _sampleCountText = string.Empty;

    [ObservableProperty]
    private int _annotatedCount;

    [ObservableProperty]
    private string _annotatedCountText = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private DateTimeOffset _lastUpdatedAt;

    [ObservableProperty]
    private string _lastUpdatedText = string.Empty;

    public string CreatedStatus { get; }

    public bool CanRename => true;

    public bool CanDelete => true;

    [ObservableProperty]
    private string? _thumbnailPath;

    [ObservableProperty]
    private ImageSource? _thumbnail;

    public bool HasThumbnail => Thumbnail is not null;

    partial void OnThumbnailChanged(ImageSource? value) => OnPropertyChanged(nameof(HasThumbnail));

    [ObservableProperty]
    private bool _isSelected;

    public event Action<GalleryRow>? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke(this);
    }

    public string AutomationId => UiAutomationIds.Gallery.Card(Id);

    public string EnterAutomationId => UiAutomationIds.Gallery.Enter(Id);

    public string EditAutomationId => UiAutomationIds.Gallery.Edit(Id);

    public string MoreAutomationId => UiAutomationIds.Gallery.More(Id);

    public static GalleryRow FromGallery(ProjectGallery gallery) => CreateFromGallery(gallery);

    public static GalleryRow CreateFromGallery(ProjectGallery gallery)
    {
        return new GalleryRow(
            gallery.Id,
            gallery.ProjectId,
            gallery.Name,
            string.IsNullOrWhiteSpace(gallery.Description) ? "暂无描述" : gallery.Description,
            gallery.SampleCount,
            gallery.AnnotatedCount,
            gallery.Progress,
            gallery.LastUpdatedAt,
            FormatRelativeTime(gallery.LastUpdatedAt),
            gallery.LastUpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            null,
            null);
    }

    /// <summary>
    /// 用最新实体原地更新标量字段，保留已加载的缩略图。
    /// </summary>
    public void ApplyFromGallery(ProjectGallery gallery)
    {
        Name = gallery.Name;
        Description = string.IsNullOrWhiteSpace(gallery.Description) ? "暂无描述" : gallery.Description;
        SampleCount = gallery.SampleCount;
        SampleCountText = $"{gallery.SampleCount} 样本";
        AnnotatedCount = gallery.AnnotatedCount;
        AnnotatedCountText = $"{gallery.AnnotatedCount} 已标注";
        Progress = gallery.Progress;
        ProgressText = gallery.Progress.ToString("P0");
        LastUpdatedAt = gallery.LastUpdatedAt;
        LastUpdatedText = FormatRelativeTime(gallery.LastUpdatedAt);
    }

    private static string FormatRelativeTime(DateTimeOffset timestamp)
    {
        TimeSpan elapsed = DateTimeOffset.Now - timestamp.ToLocalTime();
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
