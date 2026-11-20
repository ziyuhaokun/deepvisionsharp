using Avalonia.Controls;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>图库页。本波用 stub 数据，不接业务 ViewModel，也不做导入 / 保存流程。</summary>
public partial class GalleryPage : UserControl
{
    private readonly SampleGalleryContext _context = new();

    public GalleryPage()
    {
        InitializeComponent();
        DataContext = _context;
        SizeChanged += OnPageSizeChanged;
    }

    /// <summary>与 deepvision 同一套分栏公式：可用宽度按 380 一分栏，1~4 栏。</summary>
    private void OnPageSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var availableWidth = Math.Max(0, Bounds.Width - 64);
        _context.CardColumns = availableWidth > 0
            ? Math.Clamp((int)Math.Floor(availableWidth / 380), 1, 4)
            : 1;
    }
}
