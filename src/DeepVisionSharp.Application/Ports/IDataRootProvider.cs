namespace DeepVisionSharp.Application.Ports;

/// <summary>
/// Resolves the product data root (<c>%LOCALAPPDATA%\DeepVisionSharp\Data</c> by default).
/// UI tests override this via <c>--data-root</c> so each run is isolated.
/// </summary>
public interface IDataRootProvider
{
    string DataDirectory { get; }
}

public sealed class DefaultDataRootProvider : IDataRootProvider
{
    public DefaultDataRootProvider(string? dataDirectory = null)
    {
        DataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepVisionSharp",
                "Data")
            : Path.GetFullPath(dataDirectory.Trim());

        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }
}
