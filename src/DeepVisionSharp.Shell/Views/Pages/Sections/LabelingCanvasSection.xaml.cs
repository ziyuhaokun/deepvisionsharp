namespace DeepVisionSharp.Shell.Views.Pages.Sections;

public partial class LabelingCanvasSection : System.Windows.Controls.UserControl
{
    public LabelingCanvasSection() => InitializeComponent();

    private void OnZoomInClicked(object sender, System.Windows.RoutedEventArgs e) => BoxLayer.ZoomBy(1.2);

    private void OnZoomOutClicked(object sender, System.Windows.RoutedEventArgs e) => BoxLayer.ZoomBy(1 / 1.2);

    private void OnFitClicked(object sender, System.Windows.RoutedEventArgs e) => BoxLayer.FitView();
}
