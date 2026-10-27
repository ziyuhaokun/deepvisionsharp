using System.Windows;

namespace DeepVisionSharp.Shell.Views.Pages.Sections;

public partial class ModelsListSection : System.Windows.Controls.UserControl
{
    public ModelsListSection() => InitializeComponent();

    private void OnModelActionsMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }
}
