using Xunit;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Tests.Models;

/// <summary>
/// The workflow × model-family matrix is the single gate that decides which model a project may
/// train or run. These tests pin both families so adding a third one cannot silently widen it.
/// </summary>
public sealed class TaskCapabilityMatrixTests
{
    [Theory]
    [InlineData(ProjectTypes.Classification, ProjectWorkflowKind.Classification)]
    [InlineData(ProjectTypes.Detection, ProjectWorkflowKind.Detection)]
    [InlineData(" detection ", ProjectWorkflowKind.Detection)]
    [InlineData("CLASSIFICATION", ProjectWorkflowKind.Classification)]
    public void Project_workflow_resolves_supported_types(string type, ProjectWorkflowKind expected)
    {
        Assert.Equal(expected, ProjectWorkflow.Resolve(type));
    }

    [Theory]
    [InlineData("Segmentation")]
    [InlineData("Anomaly")]
    [InlineData("")]
    public void Unsupported_project_types_are_rejected(string type)
    {
        Assert.Throws<ArgumentException>(() => ProjectWorkflow.Resolve(type));
    }

    [Theory]
    [InlineData(ProjectWorkflowKind.Classification, ModelFamily.Classification, true)]
    [InlineData(ProjectWorkflowKind.Classification, ModelFamily.Detection, false)]
    [InlineData(ProjectWorkflowKind.Detection, ModelFamily.Detection, true)]
    [InlineData(ProjectWorkflowKind.Detection, ModelFamily.Classification, false)]
    public void Workflow_accepts_only_its_own_model_family(
        ProjectWorkflowKind workflow,
        ModelFamily family,
        bool expected)
    {
        Assert.Equal(expected, TaskCompatibilityCatalog.Validate(workflow, family).IsSuccess);
    }

    [Theory]
    [InlineData("yolo26n-det", ModelFamily.Detection)]
    [InlineData("yolo26-s-det", ModelFamily.Detection)]
    [InlineData("yolo26m_det", ModelFamily.Detection)]
    [InlineData("yolo26x-det", ModelFamily.Detection)]
    [InlineData("yolo26n-cls", ModelFamily.Classification)]
    [InlineData("resnet18", ModelFamily.Classification)]
    public void Model_type_resolves_to_its_family(string modelType, ModelFamily expected)
    {
        Assert.True(ModelCapabilityCatalog.TryResolve(modelType, out var capabilities));
        Assert.Equal(expected, capabilities.Family);
    }

    [Fact]
    public void Detection_capability_uses_detection_backends()
    {
        Assert.True(ModelCapabilityCatalog.TryResolve("yolo26n-det", out var capabilities));

        Assert.Equal(BackendKeys.TorchSharpDetection, capabilities.TrainingBackendKey);
        Assert.Equal(BackendKeys.InferenceTorchSharpDetection, capabilities.InferenceBackendKey);
        Assert.Equal(ArtifactHandlerKeys.Detection, capabilities.ArtifactHandlerKey);
        Assert.True(capabilities.UsesInProcessTraining);
    }

    [Fact]
    public void Detection_model_keys_are_exposed_separately_from_classification()
    {
        Assert.All(ModelTypeNames.DetectionProductKeys, key => Assert.True(ModelTypeNames.IsDetectionModelType(key)));
        Assert.All(ModelTypeNames.DetectionProductKeys, key => Assert.False(ModelTypeNames.IsClassificationModelType(key)));
        Assert.All(ModelTypeNames.ClassificationProductKeys, key => Assert.True(ModelTypeNames.IsClassificationModelType(key)));
        Assert.All(ModelTypeNames.AllProductKeys, key => Assert.True(ModelTypeNames.IsKnownModelType(key)));
    }

    [Fact]
    public void Detection_input_policy_is_letterbox_friendly()
    {
        var policy = NetworkInputSizePolicy.ForModelType(ModelTypeNames.DefaultDetection);

        Assert.NotNull(policy);
        Assert.Equal(640, policy!.Default);
        Assert.Equal(32, policy.Step);
        Assert.True(policy.IsValid(640));
        Assert.False(policy.IsValid(650));
        Assert.Equal(320, policy.Normalize(100));
    }

    [Fact]
    public void Metric_profile_follows_the_family()
    {
        Assert.Equal(MetricProfileKind.DetectionMap, MetricProfileCatalog.ForModelType("yolo26n-det"));
        Assert.Equal(MetricProfileKind.ClassificationAccuracy, MetricProfileCatalog.ForModelType("resnet18"));
        Assert.Equal(MetricProfileKind.None, MetricProfileCatalog.ForModelType("unknown"));
    }

    [Fact]
    public void Project_type_labels_cover_detection()
    {
        Assert.Equal("目标检测", ProjectTypes.GetDisplayLabel(ProjectTypes.Detection));
        Assert.Contains("框", ProjectTypes.GetDescription(ProjectTypes.Detection));
        Assert.Equal(ProjectTypes.Detection, ProjectTypes.MigrateImportedType("detection"));
    }

    [Fact]
    public void Both_project_types_are_active_but_classification_stays_the_default()
    {
        Assert.Contains(ProjectTypes.Classification, ProjectCapabilityCatalog.ActiveProjectTypes);
        Assert.Contains(ProjectTypes.Detection, ProjectCapabilityCatalog.ActiveProjectTypes);
        Assert.Equal(ProjectTypes.Classification, ProjectCapabilityCatalog.DefaultCreationType);
        Assert.True(ProjectCapabilityCatalog.IsActive(ProjectTypes.Detection));
    }
}
