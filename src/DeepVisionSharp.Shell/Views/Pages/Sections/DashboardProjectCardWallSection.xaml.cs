using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DeepVisionSharp.Shell.Views.Pages.Sections;

public partial class DashboardProjectCardWallSection : System.Windows.Controls.UserControl
{
    private const double EntranceDurationMilliseconds = 220;
    private const double EntranceStaggerMilliseconds = 40;
    private const double EntranceMaxStaggerMilliseconds = 300;
    private const double EntranceRisePixels = 10;

    public DashboardProjectCardWallSection()
    {
        InitializeComponent();
        ProjectCardsHost.ItemContainerGenerator.StatusChanged += OnProjectCardsGeneratorStatusChanged;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ProjectCardsHost.ItemContainerGenerator.StatusChanged -= OnProjectCardsGeneratorStatusChanged;
    }

    private void OnProjectActionsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } element)
        {
            return;
        }

        menu.PlacementTarget = element;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Runs a staggered fade/slide entrance for freshly generated card containers.
    /// Each container animates exactly once (handler unsubscribes after its Loaded fires),
    /// so page changes replay the effect while retained instances stay still on re-entry.
    /// </summary>
    private void OnProjectCardsGeneratorStatusChanged(object? sender, EventArgs e)
    {
        if (ProjectCardsHost.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
        {
            return;
        }

        var generator = ProjectCardsHost.ItemContainerGenerator;
        for (var i = 0; i < generator.Items.Count; i++)
        {
            if (generator.ContainerFromIndex(i) is FrameworkElement container)
            {
                container.Loaded -= OnProjectCardContainerLoaded;
                container.Loaded += OnProjectCardContainerLoaded;
            }
        }
    }

    private void OnProjectCardContainerLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement container)
        {
            return;
        }

        container.Loaded -= OnProjectCardContainerLoaded;
        var index = ProjectCardsHost.ItemContainerGenerator.IndexFromContainer(container);
        AnimateCardEntrance(container, Math.Max(0, index));
    }

    private static void AnimateCardEntrance(FrameworkElement card, int index)
    {
        if (card.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            card.RenderTransform = translate;
        }

        translate.Y = EntranceRisePixels;
        card.Opacity = 0;

        // Cap the stagger so large result sets still settle within ~0.5s total.
        var delay = Math.Min(index * EntranceStaggerMilliseconds, EntranceMaxStaggerMilliseconds);
        var storyboard = new Storyboard
        {
            BeginTime = TimeSpan.FromMilliseconds(delay)
        };

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(EntranceDurationMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, card);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fade);

        var slide = new DoubleAnimation(EntranceRisePixels, 0, TimeSpan.FromMilliseconds(EntranceDurationMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, translate);
        Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.YProperty));
        storyboard.Children.Add(slide);

        storyboard.Begin();
    }
}
