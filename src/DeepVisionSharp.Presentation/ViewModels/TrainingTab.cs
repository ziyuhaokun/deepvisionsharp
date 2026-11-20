namespace DeepVisionSharp.Presentation.ViewModels;

public enum TrainingDetailMode
{
    Settings,
    Run
}

public enum TrainingSettingsSection
{
    Basic,
    Dataset,
    Hyperparameters,
    Advanced
}

public enum TrainingRunSection
{
    Overview,
    ConfigPreview,
    Preview,
    Metrics,
    Logs,
    Resources
}
