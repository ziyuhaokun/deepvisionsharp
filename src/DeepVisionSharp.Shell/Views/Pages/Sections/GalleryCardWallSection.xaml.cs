using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DeepVisionSharp.Shell.Views.Pages.Sections;

public partial class GalleryCardWallSection : System.Windows.Controls.UserControl
{
    private const double EntranceDurationMilliseconds = 220;
    private const double EntranceStaggerMilliseconds = 40;
    private const double EntranceMaxStaggerMilliseconds = 300;
    private const double EntranceRisePixels = 10;

    public GalleryCardWallSection()
    {
        InitializeComponent();
        GalleryCardsHost.ItemContainerGenerator.StatusChanged += OnGalleryCardsGeneratorStatusChanged;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        GalleryCardsHost.ItemContainerGenerator.StatusChanged -= OnGalleryCardsGeneratorStatusChanged;
    }

    private void OnGalleryActionsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } element)
        {
            return;
        }

        menu.PlacementTarget = element;
        menu.IsOpen = true;
    }

    private void OnGalleryCardsGeneratorStatusChanged(object? sender, EventArgs e)
    {
        if (GalleryCardsHost.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
        {
            return;
        }

        var generator = GalleryCardsHost.ItemContainerGenerator;
        for (var i = 0; i < generator.Items.Count; i++)
        {
            if (generator.ContainerFromIndex(i) is FrameworkElement container)
            {
                container.Loaded -= OnGalleryCardContainerLoaded;
                container.Loaded += OnGalleryCardContainerLoaded;
            }
        }
    }

    private void OnGalleryCardContainerLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement container)
        {
            return;
        }

        container.Loaded -= OnGalleryCardContainerLoaded;
        var index = GalleryCardsHost.ItemContainerGenerator.IndexFromContainer(container);
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
