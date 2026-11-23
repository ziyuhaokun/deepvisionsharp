using Microsoft.Extensions.Logging.Abstractions;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Presentation.Ports;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.Services.Labeling;
using DeepVisionSharp.Presentation.Services.Labeling.WriteBack;
using DeepVisionSharp.Presentation.ViewModels.Labeling;
using Xunit;

namespace DeepVisionSharp.Presentation.Tests;

public sealed class LabelingWriteBackTests : IDisposable
{
    private readonly DirectoryInfo _directory;
    private readonly LabelingWorkspaceContext _workspace;
    private readonly LabelingEditorViewModel _editor;
    private readonly LabelingLabelsViewModel _labels;
    private readonly AnnotationWriter _writer = new();

    public LabelingWriteBackTests()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "DeepVisionSharp.slnx")))
        {
            repository = repository.Parent ?? throw new DirectoryNotFoundException("DeepVisionSharp.slnx");
        }

        _directory = Directory.CreateDirectory(Path.Combine(repository.FullName, "temp", $"write-back-{Guid.NewGuid():N}"));
        var paths = ShellRuntimePaths.FromOptions(new DeepVisionLaunchOptions
        {
            IsUiTest = true,
            VerificationRoot = _directory.FullName,
            DataRoot = Path.Combine(_directory.FullName, "Data")
        });
        var dispatcher = new InlineDispatcher();
        var snackbar = new ShellSnackbarNotifier(new ShellSettingsService(NullLogger<ShellSettingsService>.Instance, paths),
            dispatcher, NullLogger<ShellSnackbarNotifier>.Instance);
        var project = new Project(Guid.NewGuid(), "Project", DateTimeOffset.Now);
        // The write-back path uses active sample state and the annotation port without loading a page or image.
        _workspace = new LabelingWorkspaceContext(null!)
        {
            Project = project,
            Gallery = new ProjectGallery(Guid.NewGuid(), project.Id, "Gallery", "", 2, 0, DateTimeOffset.Now),
            ActiveSampleId = Guid.NewGuid(),
            ActiveFileName = "sample.png",
            ActiveImagePath = Path.Combine(_directory.FullName, "sample.png"),
            ImageWidth = 100,
            ImageHeight = 80
        };
        _editor = new LabelingEditorViewModel(_workspace, _writer, snackbar, NullLogger<LabelingEditorViewModel>.Instance,
            null!, dispatcher, () => Task.CompletedTask) { ImageClass = "original" };
        _labels = new LabelingLabelsViewModel(_workspace, null!, _editor, snackbar, null!, NullLogger<LabelingLabelsViewModel>.Instance);
        _labels.LabelOptions.Add(new LabelOptionRow(Guid.NewGuid(), "scratch", "#000000", "1"));
    }

    [Fact]
    public async Task Empty_detection_is_skipped_without_writing_annotations()
    {
        var result = await new BoundingBoxesWriteBackStrategy().WriteBackAsync(
            MakeContext(new InferenceResult()), CancellationToken.None);

        Assert.Equal(LabelingWriteBackStatus.Skipped, result.Status);
        Assert.Empty(_writer.Writes);
        Assert.False(_editor.IsReviewed);
    }

    [Fact]
    public async Task Unknown_class_fails_without_writing_annotations()
    {
        var result = await WriteClassAsync("unknown");

        Assert.Equal(LabelingWriteBackStatus.Failed, result.Status);
        Assert.Empty(_writer.Writes);
        Assert.Equal("original", _editor.ImageClass);
    }

    [Fact]
    public async Task Write_failure_keeps_the_existing_editor_class()
    {
        _writer.Persist = _ => throw new IOException("disk write failed");

        var result = await WriteClassAsync("scratch");

        Assert.Equal(LabelingWriteBackStatus.Failed, result.Status);
        Assert.Contains("disk write failed", result.Message);
        Assert.Equal("original", _editor.ImageClass);
        Assert.False(_workspace.IsSaving);
    }

    [Fact]
    public async Task Waiting_write_back_skips_after_the_workspace_switches_projects()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _writer.Persist = async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        };
        var firstWrite = WriteClassAsync("scratch");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var waitingWrite = WriteClassAsync("scratch");
        _workspace.Project = new Project(Guid.NewGuid(), "Other project", DateTimeOffset.Now);
        release.SetResult();

        Assert.Equal(LabelingWriteBackStatus.Saved, (await firstWrite).Status);
        Assert.Equal(LabelingWriteBackStatus.Skipped, (await waitingWrite).Status);
        Assert.Single(_writer.Writes);
        Assert.Equal("original", _editor.ImageClass);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_respects_the_store_commit_boundary(bool storeCommitted)
    {
        using var cancellation = new CancellationTokenSource();
        _writer.Persist = token =>
        {
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
            if (!storeCommitted)
            {
                token.ThrowIfCancellationRequested();
            }
            return Task.CompletedTask;
        };
        _workspace.ImageWidth = 0;
        _workspace.ImageHeight = 0;

        if (storeCommitted)
        {
            Assert.Equal(LabelingWriteBackStatus.Saved, (await WriteClassAsync("scratch", cancellation.Token)).Status);
            Assert.Equal("scratch", _editor.ImageClass);
            var write = Assert.Single(_writer.Writes);
            Assert.Equal((100d, 80d), (write.Item.ImageWidth, write.Item.ImageHeight));
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WriteClassAsync("scratch", cancellation.Token));
            Assert.Empty(_writer.Writes);
            Assert.Equal("original", _editor.ImageClass);
        }
        Assert.False(_workspace.IsSaving);
    }

    [Fact]
    public async Task Manual_save_failure_keeps_the_edit_visible_and_can_be_retried()
    {
        _writer.Persist = _ => throw new IOException("disk write failed");
        Assert.False(await _editor.ApplyImageClassAndSaveAsync("scratch"));
        Assert.Equal("scratch", _editor.ImageClass);
        Assert.True(_workspace.HasAnnotationSaveError);

        _writer.Persist = _ => Task.CompletedTask;
        await _editor.SaveCommand.ExecuteAsync(null);

        Assert.False(_workspace.HasAnnotationSaveError);
        Assert.Equal("scratch", Assert.Single(_writer.Writes).Item.ImageClass);
    }

    public void Dispose() => _directory.Delete(recursive: true);

    private Task<LabelingWriteBackResult> WriteClassAsync(string name, CancellationToken cancellationToken = default) =>
        new ImageClassTop1WriteBackStrategy().WriteBackAsync(MakeContext(new InferenceResult
        {
            ImageWidth = 100,
            ImageHeight = 80,
            Detections = [new ClassificationResult { ClassName = name, Confidence = 0.95 }]
        }), cancellationToken);

    private LabelingWriteBackContext MakeContext(InferenceResult inference) => new()
    {
        Inference = inference,
        Target = LabelingSampleIdentity.Capture(_workspace),
        Workspace = _workspace,
        Editor = _editor,
        Labels = _labels
    };

    private sealed class AnnotationWriter : IAnnotationWorkflowService
    {
        public Func<CancellationToken, Task> Persist { get; set; } = _ => Task.CompletedTask;
        public List<(Guid GalleryId, AnnotationSaveItem Item)> Writes { get; } = [];

        public async Task SaveAsync(Guid galleryId, string fileName, double imageWidth, double imageHeight,
            string? imageClass, IReadOnlyList<AnnotationShape>? shapes, bool isReviewed, CancellationToken cancellationToken)
        {
            await Persist(cancellationToken);
            Writes.Add((galleryId, new AnnotationSaveItem(fileName, imageWidth, imageHeight, imageClass, shapes, isReviewed)));
        }

        public Task SaveAsync(Guid galleryId, string fileName, double imageWidth, double imageHeight, string? imageClass,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnnotationDocument> LoadAsync(Guid galleryId, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, string>> LoadImageClassesBatchAsync(Guid galleryId, IReadOnlyList<string> fileNames,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, AnnotationDocument>> LoadDocumentsBatchAsync(Guid galleryId, IReadOnlyList<string> fileNames,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveBatchAsync(Guid galleryId, IReadOnlyList<AnnotationSaveItem> items, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteSampleAsync(Guid galleryId, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public string GetImagePath(Guid projectId, Guid galleryId, string fileName) => throw new NotSupportedException();
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public void Invoke(Action action) => action();
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
        public Task InvokeAsync(Func<Task> action) => action();
        public Task InvokeAsync(Action action, UiDispatchPriority priority) => InvokeAsync(action);
        public Task InvokeAsync(Func<Task> action, UiDispatchPriority priority) => InvokeAsync(action);
    }
}
