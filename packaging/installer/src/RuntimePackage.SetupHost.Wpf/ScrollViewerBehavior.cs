using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RuntimePackage.SetupHost;

public static class ScrollViewerBehavior
{
    public static readonly DependencyProperty AutoScrollToBottomProperty =
        DependencyProperty.RegisterAttached(
            "AutoScrollToBottom",
            typeof(bool),
            typeof(ScrollViewerBehavior),
            new PropertyMetadata(false, OnAutoScrollToBottomChanged));

    public static bool GetAutoScrollToBottom(DependencyObject element) =>
        (bool)element.GetValue(AutoScrollToBottomProperty);

    public static void SetAutoScrollToBottom(DependencyObject element, bool value) =>
        element.SetValue(AutoScrollToBottomProperty, value);

    private static void OnAutoScrollToBottomChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.Loaded -= OnLoaded;
        scrollViewer.ScrollChanged -= OnScrollChanged;

        if (e.NewValue is true)
        {
            scrollViewer.Loaded += OnLoaded;
            scrollViewer.ScrollChanged += OnScrollChanged;
            ScrollToBottom(scrollViewer);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            ScrollToBottom(scrollViewer);
        }
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer && (e.ExtentHeightChange > 0 || e.ViewportHeightChange > 0))
        {
            ScrollToBottom(scrollViewer);
        }
    }

    private static void ScrollToBottom(ScrollViewer scrollViewer)
    {
        scrollViewer.Dispatcher.BeginInvoke(
            () => scrollViewer.ScrollToEnd(),
            DispatcherPriority.Background);
    }
}
