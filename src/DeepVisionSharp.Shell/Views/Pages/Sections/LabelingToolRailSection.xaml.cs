using System.Windows;

namespace DeepVisionSharp.Shell.Views.Pages.Sections;

public partial class LabelingToolRailSection : System.Windows.Controls.UserControl
{
    public LabelingToolRailSection() => InitializeComponent();

    private void OnShortcutsHelpClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } element)
        {
            menu.PlacementTarget = element;
            menu.IsOpen = true;
        }
    }
}
