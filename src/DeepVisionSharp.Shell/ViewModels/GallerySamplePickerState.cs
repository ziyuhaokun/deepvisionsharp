using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace DeepVisionSharp.Shell.ViewModels;

public sealed partial class GallerySamplePickerState : ObservableObject
{
    public const int PageSize = 20;

    [ObservableProperty]
    private int _pageIndex;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _galleryName = string.Empty;

    [ObservableProperty]
    private string _dialogErrorMessage = string.Empty;

    public ObservableCollection<GallerySamplePickerRow> PageItems { get; } = new();

    public HashSet<string> SelectedFileNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int SelectedCount => SelectedFileNames.Count;

    public int PageCount => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool CanGoToPreviousPage => PageIndex > 0;

    public bool CanGoToNextPage => PageIndex < PageCount - 1;

    public string PageText => PageCount == 0 ? "0 / 0" : $"{PageIndex + 1} / {PageCount}";

    public string SelectionSummary => $"已选择 {SelectedCount} / {TotalCount}";

    public bool HasDialogErrorMessage => !string.IsNullOrWhiteSpace(DialogErrorMessage);

    public bool HasNoPageItems => PageItems.Count == 0;

    public void Reset(string galleryName)
    {
        GalleryName = galleryName;
        PageIndex = 0;
        TotalCount = 0;
        PageItems.Clear();
        SelectedFileNames.Clear();
        DialogErrorMessage = string.Empty;
        NotifyStateChanged();
    }

    public void SetDialogError(string message)
    {
        DialogErrorMessage = message;
        OnPropertyChanged(nameof(HasDialogErrorMessage));
    }

    public void ClearDialogError()
    {
        DialogErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasDialogErrorMessage));
    }

    public void ToggleSelection(string fileName, bool isSelected)
    {
        if (isSelected)
        {
            SelectedFileNames.Add(fileName);
        }
        else
        {
            SelectedFileNames.Remove(fileName);
        }

        var row = PageItems.FirstOrDefault(item =>
            string.Equals(item.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            row.IsSelected = isSelected;
        }

        NotifyStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private void GoToPreviousPage()
    {
        if (PageIndex > 0)
        {
            PageIndex--;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private void GoToNextPage()
    {
        if (PageIndex < PageCount - 1)
        {
            PageIndex++;
        }
    }

    [RelayCommand]
    private void SelectAllOnPage()
    {
        foreach (var row in PageItems)
        {
            row.IsSelected = true;
            SelectedFileNames.Add(row.FileName);
        }

        NotifyStateChanged();
    }

    [RelayCommand]
    private void ClearPageSelection()
    {
        foreach (var row in PageItems)
        {
            row.IsSelected = false;
            SelectedFileNames.Remove(row.FileName);
        }

        NotifyStateChanged();
    }

    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        OnPropertyChanged(nameof(HasNoPageItems));
    }
}

public sealed partial class GallerySamplePickerRow : ObservableObject
{
    public GallerySamplePickerRow(Guid id, string fileName, bool isAnnotated, bool isSelected)
    {
        Id = id;
        FileName = fileName;
        IsAnnotated = isAnnotated;
        _isSelected = isSelected;
    }

    public Guid Id { get; }

    public string FileName { get; }

    public bool IsAnnotated { get; }

    [ObservableProperty]
    private bool _isSelected;

    public event Action<GallerySamplePickerRow>? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke(this);
    }

    public string AnnotatedText => IsAnnotated ? "已标注" : "未标注";
}
