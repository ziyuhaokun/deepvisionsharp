using DeepVisionSharp.TorchSharp.Classification.Pretrained;
using DeepVisionSharp.TorchSharp.Models;

if (args.Length != 4 || PretrainedWeightsCatalog.ForArch(args[0]) is not { } source)
{
    Console.Error.WriteLine("Specify a supported architecture, destination, source path and whether to keep the download.");
    return 2;
}

var destination = string.IsNullOrWhiteSpace(args[1]) ? PretrainedWeightsLocator.RootDirectory : args[1];
var sourcePath = string.IsNullOrWhiteSpace(args[2]) ? null : args[2];
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var downloader = new PretrainedWeightsDownloader(destination);
    var target = await downloader.DownloadAsync(
        source, cancellationToken: cancellation.Token, sourcePath: sourcePath, keepSource: args[3] == "true");
    Console.WriteLine($"Pretrained bundle ready: {target}");
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Pretrained weight preparation cancelled.");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
