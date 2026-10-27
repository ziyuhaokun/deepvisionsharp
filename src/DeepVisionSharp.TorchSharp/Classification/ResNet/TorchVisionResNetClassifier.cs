using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace DeepVisionSharp.TorchSharp.Classification.ResNet;

/// <summary>
/// ResNet with torchvision-compatible module names and stem (7x7/2 + maxpool).
/// </summary>
/// <remarks>
/// Field names (<c>conv1</c>, <c>bn1</c>, <c>layer1..4</c>, <c>fc</c>, block <c>conv1/bn1/conv2/bn2</c>,
/// <c>downsample.0/1</c>) match the torchvision state dict, which removes the need for key mapping.
/// </remarks>
public sealed class TorchVisionResNetClassifier : Module<Tensor, Tensor>
{
    private readonly Conv2d conv1;
    private readonly BatchNorm2d bn1;
    private readonly Sequential layer1;
    private readonly Sequential layer2;
    private readonly Sequential layer3;
    private readonly Sequential layer4;
    private readonly AdaptiveAvgPool2d avgpool;
    private readonly Linear fc;
    private readonly string _archName;
    private readonly int _numClasses;

    public TorchVisionResNetClassifier(
        string archName,
        int inputChannels,
        int numClasses,
        IReadOnlyList<int> stageChannels,
        IReadOnlyList<int> stageBlocks)
        : base(nameof(TorchVisionResNetClassifier))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archName);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputChannels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(numClasses, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(stageChannels.Count, 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(stageBlocks.Count, 4);

        _archName = archName;
        _numClasses = numClasses;

        conv1 = Conv2d(inputChannels, stageChannels[0], kernel_size: 7, stride: 2, padding: 3, bias: false);
        bn1 = BatchNorm2d(stageChannels[0]);
        layer1 = MakeLayer(stageChannels[0], stageChannels[0], stageBlocks[0], stride: 1);
        layer2 = MakeLayer(stageChannels[0], stageChannels[1], stageBlocks[1], stride: 2);
        layer3 = MakeLayer(stageChannels[1], stageChannels[2], stageBlocks[2], stride: 2);
        layer4 = MakeLayer(stageChannels[2], stageChannels[3], stageBlocks[3], stride: 2);
        avgpool = AdaptiveAvgPool2d([1, 1]);
        fc = Linear(stageChannels[3], numClasses);

        RegisterComponents();
        InitializeWeights();
    }

    public string ArchName => _archName;

    public int NumClasses => _numClasses;

    public override Tensor forward(Tensor inputTensor)
    {
        using var scope = NewDisposeScope();
        var featureMap = functional.relu(bn1.forward(conv1.forward(inputTensor)));
        featureMap = functional.max_pool2d(featureMap, 3, 2, 1);
        featureMap = layer1.forward(featureMap);
        featureMap = layer2.forward(featureMap);
        featureMap = layer3.forward(featureMap);
        featureMap = layer4.forward(featureMap);

        var pooled = avgpool.forward(featureMap).flatten(1);
        var logits = fc.forward(pooled);
        return logits.MoveToOuterDisposeScope();
    }

    public static TorchVisionResNetClassifier CreateTiny(int inputChannels, int numClasses) =>
        new("resnet_tiny_v2", inputChannels, numClasses, [16, 32, 64, 128], [2, 2, 2, 2]);

    public static TorchVisionResNetClassifier CreateResNet18(int inputChannels, int numClasses) =>
        new("resnet18_v2", inputChannels, numClasses, [64, 128, 256, 512], [2, 2, 2, 2]);

    public static TorchVisionResNetClassifier CreateResNet34(int inputChannels, int numClasses) =>
        new("resnet34_v2", inputChannels, numClasses, [64, 128, 256, 512], [3, 4, 6, 3]);

    private static Sequential MakeLayer(int inPlanes, int planes, int blocks, int stride)
    {
        var modules = new List<(string, Module<Tensor, Tensor>)>
        {
            ("0", new TorchVisionBasicBlock(inPlanes, planes, stride))
        };
        for (var blockIndex = 1; blockIndex < blocks; blockIndex++)
        {
            modules.Add((blockIndex.ToString(), new TorchVisionBasicBlock(planes, planes, stride: 1)));
        }

        return Sequential(modules);
    }

    private void InitializeWeights()
    {
        foreach (var namedModule in named_modules())
        {
            switch (namedModule.module)
            {
                case Conv2d convModule:
                    init.kaiming_normal_(convModule.weight, mode: init.FanInOut.FanOut, nonlinearity: init.NonlinearityType.ReLU);
                    if (convModule.bias is not null)
                    {
                        init.zeros_(convModule.bias);
                    }

                    break;
                case BatchNorm2d bnModule:
                    if (bnModule.weight is not null)
                    {
                        init.ones_(bnModule.weight);
                    }

                    if (bnModule.bias is not null)
                    {
                        init.zeros_(bnModule.bias);
                    }

                    break;
                case Linear linearModule:
                    init.normal_(linearModule.weight, 0, 0.01);
                    if (linearModule.bias is not null)
                    {
                        init.zeros_(linearModule.bias);
                    }

                    break;
            }
        }
    }
}

/// <summary>BasicBlock with torchvision field names and the zero-initialized residual BN.</summary>
public sealed class TorchVisionBasicBlock : Module<Tensor, Tensor>
{
    private readonly Conv2d conv1;
    private readonly BatchNorm2d bn1;
    private readonly Conv2d conv2;
    private readonly BatchNorm2d bn2;
    private readonly Sequential? downsample;

    public TorchVisionBasicBlock(int inPlanes, int planes, int stride = 1)
        : base(nameof(TorchVisionBasicBlock))
    {
        conv1 = Conv2d(inPlanes, planes, kernel_size: 3, stride: stride, padding: 1, bias: false);
        bn1 = BatchNorm2d(planes);
        conv2 = Conv2d(planes, planes, kernel_size: 3, stride: 1, padding: 1, bias: false);
        bn2 = BatchNorm2d(planes);

        if (stride != 1 || inPlanes != planes)
        {
            downsample = Sequential(
                ("0", Conv2d(inPlanes, planes, kernel_size: 1, stride: stride, bias: false)),
                ("1", BatchNorm2d(planes)));
        }

        RegisterComponents();

        // torchvision zero-initializes the last BN of each block so the residual branch starts at 0.
        if (bn2.weight is not null)
        {
            init.zeros_(bn2.weight);
        }
    }

    public override Tensor forward(Tensor inputTensor)
    {
        using var scope = NewDisposeScope();
        var featureMap = functional.relu(bn1.forward(conv1.forward(inputTensor)));
        featureMap = bn2.forward(conv2.forward(featureMap));

        var residual = downsample is not null ? downsample.forward(inputTensor) : inputTensor;
        featureMap = functional.relu(featureMap + residual);

        return featureMap.MoveToOuterDisposeScope();
    }
}
