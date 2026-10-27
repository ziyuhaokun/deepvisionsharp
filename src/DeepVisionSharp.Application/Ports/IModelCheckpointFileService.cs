namespace DeepVisionSharp.Application.Ports;

public interface IModelCheckpointFileService
{
    bool Exists(string? checkpointPath);

    bool TryGetDirectory(string checkpointPath, out string directory);

    /// <summary>只导出 PyTorch 权重与 sidecar（<c>.pt</c> + <c>.pt.info</c>），不夹带 IR 目录。</summary>
    string? Export(string checkpointPath, string exportRoot, string folderName);

    void DeleteFiles(string checkpointPath);
}
