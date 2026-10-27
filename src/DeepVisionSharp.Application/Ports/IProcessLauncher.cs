namespace DeepVisionSharp.Application.Ports;

public interface IProcessLauncher
{
    bool TryOpenFolder(string folderPath);
}
