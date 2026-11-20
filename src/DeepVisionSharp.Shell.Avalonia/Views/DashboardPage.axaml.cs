using Avalonia.Controls;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>首页（项目）页面。本波用 stub 数据，不接业务 ViewModel。</summary>
public partial class DashboardPage : UserControl
{
    private readonly SampleDashboardContext _context = new();

    public DashboardPage()
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
