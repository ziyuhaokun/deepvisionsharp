using Xunit;
using FluentValidation.TestHelper;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Application.Validators;

namespace DeepVisionSharp.Application.Tests.Validators;

public sealed class CommandValidatorTests
{
    private readonly RunSingleInferenceCommandValidator _inferenceValidator = new();
    private readonly CreateTrainingJobCommandValidator _trainingValidator = new();

    [Theory]
    [InlineData("resnet18")]
    [InlineData("resnet-tiny")]
    [InlineData("resnet34")]
    [InlineData("yolo26n-cls")]
    [InlineData("yolo26s-cls")]
    [InlineData("yolo26m-cls")]
    [InlineData("yolo26l-cls")]
    [InlineData("yolo26x-cls")]
    [InlineData("yolo26n")]
    [InlineData("yolo26-s")]
    public void InferenceValidator_accepts_valid_model_types(string modelType)
    {
        var command = new RunSingleInferenceCommand(
            ModelPath: "models/best.torchsharp",
            ImagePath: "samples/001.jpg",
            Config: new InferenceConfig(),
            ModelType: modelType);

        var result = _inferenceValidator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.ModelType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown_model_type")]
    [InlineData("yolo_v5_invalid")]
    public void InferenceValidator_rejects_invalid_model_types(string modelType)
    {
        var command = new RunSingleInferenceCommand(
            ModelPath: "models/best.torchsharp",
            ImagePath: "samples/001.jpg",
            Config: new InferenceConfig(),
            ModelType: modelType);

        var result = _inferenceValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.ModelType);
    }

    [Fact]
    public void InferenceValidator_rejects_empty_paths()
    {
        var command = new RunSingleInferenceCommand(
            ModelPath: "",
            ImagePath: "",
            Config: new InferenceConfig(),
            ModelType: "resnet18");

        var result = _inferenceValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.ModelPath);
        result.ShouldHaveValidationErrorFor(c => c.ImagePath);
    }

    [Fact]
    public void TrainingValidator_accepts_valid_job_creation()
    {
        var command = new CreateTrainingJobCommand(
            Name: "experiment_01",
            ModelType: "yolo26n-cls",
            ProjectId: Guid.NewGuid());

        var result = _trainingValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TrainingValidator_rejects_empty_name(string name)
    {
        var command = new CreateTrainingJobCommand(
            Name: name,
            ModelType: "resnet18",
            ProjectId: Guid.NewGuid());

        var result = _trainingValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Name);
    }
}
