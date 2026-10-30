using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Shell.Services;

namespace DeepVisionSharp.Shell.ViewModels.Training;

public sealed partial class TrainingJobsViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly WorkspaceSessionService _workspaceSession;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly DeepVisionLaunchOptions _launchOptions;
    private readonly ILogger<TrainingJobsViewModel> _logger;

    /// <summary>Survives SelectedJob=null during list rebuild so concurrent reloads keep the same target.</summary>
    private Guid? _pinnedSelectedJobId;

    /// <summary>Monotonic reload generation; older in-flight reloads bail out when superseded.</summary>
    private int _reloadGeneration;
    private int _selectionGeneration;
    private CancellationTokenSource _pageOperations = new();

    public sealed record PageOperation(CancellationToken Token, long SessionVersion, Guid? ProjectId);
    public PageOperation CapturePageOperation() => new(_pageOperations.Token, _workspaceSession.Current.Version, CurrentProjectId);
    public bool IsCurrent(PageOperation operation) => !operation.Token.IsCancellationRequested
        && _workspaceSession.IsCurrent(operation.SessionVersion) && CurrentProjectId == operation.ProjectId;

    // During a list rebuild SelectedJob is temporarily null; the pin remains the user's target.
    public bool IsJobSelected(Guid jobId) => (SelectedJob?.Id ?? _pinnedSelectedJobId) == jobId;

    private readonly DispatcherTimer _elapsedTimer;

    /// <summary>True while Jobs is being rebuilt; suppresses "请选择任务" flash.</summary>
    private bool _isJobsReloadInProgress;

    public TrainingJobsViewModel(
        IMediator mediator,
        WorkspaceSessionService workspaceSession,
        ShellSnackbarNotifier snackbar,
        IUiDispatcher uiDispatcher,
        DeepVisionLaunchOptions launchOptions,
        ILogger<TrainingJobsViewModel> logger)
    {
        _mediator = mediator;
        _workspaceSession = workspaceSession;
        _snackbar = snackbar;
        _uiDispatcher = uiDispatcher;
        _launchOptions = launchOptions;
        _logger = logger;

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += OnElapsedTimerTick;
    }

    /// <summary>Raised every second while any job is preparing or running.</summary>
    public event Action? ActiveJobElapsedTick;

    public ObservableCollection<TrainingJobListItem> Jobs { get; } = new();

    public ObservableCollection<GalleryOption> Galleries { get; } = new();

    [ObservableProperty]
    private bool _hasProjectContext;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private TrainingJobListItem? _selectedJob;

    [ObservableProperty]
    private int _jobCount;

    public Guid? CurrentProjectId { get; private set; }

    /// <summary>Stored type of the project in context; null while no project is open.</summary>
    public string? CurrentProjectType { get; private set; }

    public bool IsTrainingWorkflow => HasProjectContext;

    public bool HasJobs => JobCount > 0;

    public bool HasEmptyJobList => HasProjectContext && !HasJobs;

    public bool ShowProjectRequired => !HasProjectContext;

    public bool HasSelectedJob => SelectedJob is not null;

    /// <summary>
    /// Detail panel (settings/run) is only shown after form state is fully applied.
    /// Showing it in the same layout pass as SelectedJob assignment hard-crashed the process
    /// (training-load-trace: died at "selecting first job").
    /// </summary>
    [ObservableProperty]
    private bool _isDetailFormReady;

    public bool ShowSelectJobPrompt =>
        HasProjectContext && !HasSelectedJob && !_isJobsReloadInProgress && _pinnedSelectedJobId is null;

    /// <summary>Selected but form not ready yet — keep detail panel out of the visual tree.</summary>
    public bool ShowDetailPreparing =>
        HasProjectContext
        && !IsDetailFormReady
        && (HasSelectedJob || _isJobsReloadInProgress || _pinnedSelectedJobId is not null);

    public bool ShowJobDetail => HasSelectedJob && IsDetailFormReady;

    public event Action<TrainingJobListItem?>? SelectedJobChanged;

    public event Action<Guid>? JobCreated;

    /// <summary>Raised after <see cref="Galleries"/> is replaced (Dataset must re-attach handlers).</summary>
    public event Action? GalleriesReloaded;

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        TraceLoad("load: begin");

        try
        {
            var session = _workspaceSession.Current;
            if (session.Project is null)
            {
                // Only clear when we know there is no project — never clear at the start of an
                // async load (NavigationView unload cancels Load and used to leave Galleries empty).
                Jobs.Clear();
                Galleries.Clear();
                SelectedJob = null;
                _pinnedSelectedJobId = null;
                CurrentProjectId = null;
                CurrentProjectType = null;
                HasProjectContext = false;
                OnPropertyChanged(nameof(CurrentProjectType));
                RefreshJobMetrics();
                NotifyListState();
                TraceLoad("load: no project context");
                return;
            }

            TraceLoad($"load: get project {session.Project.Id}");
            var projectResult = await _mediator.Send(
                new GetProjectByIdQuery(session.Project.Id),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_workspaceSession.IsCurrent(session.Version))
            {
                TraceLoad("load: session stale after get project");
                return;
            }

            if (projectResult.IsFailure)
            {
                throw new InvalidOperationException(projectResult.Error ?? "加载项目失败");
            }

            var project = projectResult.Value;
            if (project is null)
            {
                _workspaceSession.ClearProjectIfCurrent(session.Project.Id);
                Jobs.Clear();
                Galleries.Clear();
                SelectedJob = null;
                _pinnedSelectedJobId = null;
                CurrentProjectId = null;
                CurrentProjectType = null;
                HasProjectContext = false;
                OnPropertyChanged(nameof(CurrentProjectType));
                RefreshJobMetrics();
                NotifyListState();
                TraceLoad("load: project missing");
                return;
            }

            CurrentProjectId = project.Id;
            CurrentProjectType = project.Type;
            HasProjectContext = true;
            OnPropertyChanged(nameof(CurrentProjectType));
            TraceLoad($"load: {project.Type} workflow");

            TraceLoad("load: reconcile orphans");
            await _mediator.Send(new ReconcileOrphanedTrainingJobsCommand(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_workspaceSession.IsCurrent(session.Version))
            {
                TraceLoad("load: session stale after reconcile");
                return;
            }

            TraceLoad("load: galleries");
            var galleriesResult = await _mediator.Send(
                new GetGalleriesByProjectQuery(project.Id),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_workspaceSession.IsCurrent(session.Version))
            {
                TraceLoad("load: session stale after galleries");
                return;
            }
            if (galleriesResult.IsFailure || galleriesResult.Value is null)
            {
                throw new InvalidOperationException(galleriesResult.Error ?? "加载图库列表失败");
            }

            var nextGalleries = new List<GalleryOption>();
            foreach (var gallery in galleriesResult.Value)
            {
                var includeGallery = gallery.SampleCount > 0 && gallery.AnnotatedCount > 0;
                if (!includeGallery)
                {
                    continue;
                }

                nextGalleries.Add(new GalleryOption(
                    gallery.Id,
                    gallery.Name,
                    gallery.SampleCount,
                    gallery.AnnotatedCount));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!_workspaceSession.IsCurrent(session.Version))
            {
                TraceLoad("load: session stale before applying galleries");
                return;
            }

            Galleries.Clear();
            foreach (var gallery in nextGalleries)
            {
                Galleries.Add(gallery);
            }

            TraceLoad($"load: reload jobs (galleries={Galleries.Count})");
            // Do not auto-select during list rebuild — selection applies the full settings form
            // and must happen after the page layout settles (see deferred select below).
            await ReloadJobsAsync(
                cancellationToken,
                session.Version,
                preferredSelectedJobId: null,
                selectJob: false);
            if (!_workspaceSession.IsCurrent(session.Version))
            {
                TraceLoad("load: session stale after reload jobs");
                return;
            }

            // CRITICAL: do NOT auto-select a job here.
            // Selecting applies TrainingSettingsPanel (many WPF-UI NumberBoxes + library refresh).
            // Trace proved the process dies at "selecting first job" with no managed exception —
            // so opening the training page must leave selection empty; user/create picks a job later.
            TraceLoad($"load: completed jobs={Jobs.Count} (no auto-select)");

        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TraceLoad("load: canceled");
            _logger.LogDebug("Training page load was canceled");
        }
        catch (Exception ex)
        {
            TraceLoad($"load: failed {ex.GetType().Name}: {ex.Message}");
            _logger.LogError(ex, "Failed to load training page");
            _snackbar.ShowError($"加载训练页失败：{ex.Message}", "训练");
        }
        finally
        {
            IsLoading = false;
            NotifyListState();
            TraceLoad("load: finally");
        }
    }

    /// <summary>UI-test-only breadcrumbs for hard crashes that skip the managed logger.</summary>
    private void TraceLoad(string message)
    {
        try
        {
            _logger.LogInformation("Training {Message}", message);
            if (!_launchOptions.IsUiTest)
            {
                return;
            }

            var dir = ShellRuntimePaths.FromOptions(_launchOptions).LogDirectory;
            Directory.CreateDirectory(dir);
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [TRACE] {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(dir, "training-load-trace.log"), line);
        }
        catch
        {
            // never throw from diagnostics
        }
    }

    /// <summary>UI-test-only diagnostic dump for gallery reload state (OdDataRootProbe reads it).</summary>
    private void WriteReloadGalleriesDiagnostic()
    {
        if (!_launchOptions.IsUiTest)
        {
            return;
        }

        try
        {
            var path = Path.Combine(
                ShellRuntimePaths.FromOptions(_launchOptions).DiagnosticDirectory,
                $"dv-ui-reload-galleries-{Environment.ProcessId}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                $"count={Galleries.Count}; detailReady={IsDetailFormReady}; "
                + string.Join(", ", Galleries.Select(g => $"{g.Name}:{g.AnnotatedCount}/{g.SampleCount}:sel={g.IsSelected}")));
        }
        catch
        {
            // diagnostic only
        }
    }

    [RelayCommand]
    private async Task CreateJobAsync()
    {
        if (CurrentProjectId is null)
        {
            _snackbar.ShowWarning("请先选择项目。", "训练");
            return;
        }

        try
        {
            var name = BuildUniqueJobName();
            var result = await _mediator.Send(new CreateTrainingJobCommand(
                name,
                ModelTypeNames.Default,
                CurrentProjectId));
            if (result.IsFailure || result.Value == Guid.Empty)
            {
                _snackbar.ShowError(result.Error ?? "创建训练任务失败", "训练");
                return;
            }

            _logger.LogInformation(
                "Created training job {JobId} ({JobName}) for project {ProjectId}",
                result.Value,
                name,
                CurrentProjectId);

            TraceLoad($"create: reload list {result.Value}");
            await ReloadJobsAsync(preferredSelectedJobId: result.Value, selectJob: false);
            // Populate Galleries before revealing the detail template so the first ItemsSource
            // materialization already has items.
            await ReloadAnnotatedGalleriesAsync();
            await Task.Yield();
            var created = Jobs.FirstOrDefault(job => job.Id == result.Value);
            TraceLoad($"create: select {created?.Id} galleries={Galleries.Count}");
            SelectedJob = created;
            JobCreated?.Invoke(result.Value);

            // Selection applies form on ApplicationIdle (may clear gallery IsSelected for a new draft).
            await WaitForUiIdleAsync();
            AutoSelectSoleAnnotatedGallery();
            await WaitForUiIdleAsync();
            TraceLoad($"create: done galleries={Galleries.Count} detailReady={IsDetailFormReady} selected={Galleries.Count(g => g.IsSelected)}");
            WriteReloadGalleriesDiagnostic();

            _snackbar.ShowSuccess(
                Galleries.Count > 0
                    ? $"已创建训练任务（可用图库 {Galleries.Count}）。"
                    : "已创建训练任务。",
                "训练");
        }
        catch (Exception ex)
        {
            TraceLoad($"create: failed {ex.GetType().Name}: {ex.Message}");
            _logger.LogError(ex, "Create training job failed for project {ProjectId}", CurrentProjectId);
            _snackbar.ShowError($"创建训练任务失败：{ex.Message}", "训练");
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedJob))]
    private async Task DeleteJobAsync()
    {
        if (SelectedJob is null)
        {
            return;
        }

        await DeleteJobCoreAsync(SelectedJob);
    }

    public async Task DeleteJobAsync(Guid jobId)
    {
        var job = Jobs.FirstOrDefault(item => item.Id == jobId);
        if (job is null)
        {
            return;
        }

        await DeleteJobCoreAsync(job);
    }

    public bool CanDeleteSelectedJob => SelectedJob is not null;

    public void NotifyDeleteCanExecuteChanged() => DeleteJobCommand.NotifyCanExecuteChanged();

    public event Action<Guid>? JobDeleted;

    [RelayCommand]
    private async Task DuplicateJobAsync()
    {
        if (SelectedJob is null)
        {
            return;
        }

        await DuplicateJobAsync(SelectedJob.Id);
    }

    public async Task DuplicateJobAsync(Guid jobId)
    {
        var result = await _mediator.Send(new DuplicateTrainingJobCommand(jobId, CurrentProjectId));
        if (result.IsFailure || result.Value == Guid.Empty)
        {
            _snackbar.ShowError(result.Error ?? "复制训练任务失败", "训练");
            return;
        }

        await ReloadJobsAsync();
        SelectedJob = Jobs.FirstOrDefault(job => job.Id == result.Value);
        _snackbar.ShowSuccess("已复制为新草稿任务。", "训练");
    }

    public async Task ReloadAnnotatedGalleriesAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentProjectId is null)
        {
            return;
        }

        var galleriesResult = await _mediator.Send(
            new GetGalleriesByProjectQuery(CurrentProjectId.Value),
            cancellationToken);
        if (galleriesResult.IsFailure || galleriesResult.Value is null)
        {
            TraceLoad($"reload-galleries: failed {galleriesResult.Error}");
            return;
        }

        var previousById = Galleries.ToDictionary(gallery => gallery.Id);
        var nextGalleries = new List<GalleryOption>();
        foreach (var gallery in galleriesResult.Value)
        {
            var includeGallery = gallery.SampleCount > 0 && gallery.AnnotatedCount > 0;
            if (!includeGallery)
            {
                continue;
            }

            var option = new GalleryOption(
                gallery.Id,
                gallery.Name,
                gallery.SampleCount,
                gallery.AnnotatedCount);

            if (previousById.TryGetValue(gallery.Id, out var previous))
            {
                option.IsSelected = previous.IsSelected;
                option.Role = previous.Role;
            }

            nextGalleries.Add(option);
        }

        ApplyGalleriesOnUiThread(nextGalleries);
        GalleriesReloaded?.Invoke();

        TraceLoad($"reload-galleries: applied {Galleries.Count} detailReady={IsDetailFormReady}");
        WriteReloadGalleriesDiagnostic();

        if (Galleries.Count == 0)
        {
            _snackbar.ShowWarning(
                "无已标注图库可训练",
                "训练");
        }
    }

    /// <summary>
    /// New drafts start with no gallery selection. When only one annotated gallery exists,
    /// select it so Dataset estimates / Start gating work without an extra click.
    /// </summary>
    private void AutoSelectSoleAnnotatedGallery()
    {
        if (Galleries.Count != 1)
        {
            return;
        }

        var only = Galleries[0];
        if (only.IsSelected)
        {
            return;
        }

        only.IsSelected = true;
        GalleriesReloaded?.Invoke();
        TraceLoad($"create: auto-selected sole gallery {only.Name}");
    }

    public async Task ReloadJobsAsync(
        CancellationToken cancellationToken = default,
        long? expectedSessionVersion = null,
        Guid? preferredSelectedJobId = null,
        bool selectJob = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CurrentProjectId is null)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _reloadGeneration);

        if (preferredSelectedJobId is Guid preferred)
        {
            _pinnedSelectedJobId = preferred;
        }
        else if (SelectedJob?.Id is Guid current)
        {
            _pinnedSelectedJobId = current;
        }

        var selectedId = _pinnedSelectedJobId;
        TraceLoad($"reload: begin select={selectJob} preferred={preferredSelectedJobId} pin={selectedId} gen={generation}");

        _isJobsReloadInProgress = true;
        NotifyListState();

        // Clearing SelectedJob first prevents WPF ListBox two-way SelectedItem binding from
        // referencing an item no longer in ItemsSource (can crash/assert).
        SelectedJob = null;
        Jobs.Clear();

        try
        {
            var jobsResult = await _mediator.Send(
                new GetTrainingJobsByProjectQuery(CurrentProjectId.Value),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (expectedSessionVersion is not null
                && !_workspaceSession.IsCurrent(expectedSessionVersion.Value))
            {
                TraceLoad($"reload: session stale gen={generation}");
                return;
            }

            if (generation != Volatile.Read(ref _reloadGeneration))
            {
                TraceLoad($"reload: superseded after fetch gen={generation}");
                return;
            }

            if (jobsResult.IsSuccess && jobsResult.Value is not null)
            {
                foreach (var job in jobsResult.Value)
                {
                    Jobs.Add(TrainingJobUi.MapListItem(job));
                }
            }

            RefreshJobMetrics();
            TraceLoad($"reload: listed {Jobs.Count} jobs gen={generation}");

            if (!selectJob)
            {
                return;
            }

            var next = selectedId is null
                ? Jobs.FirstOrDefault()
                : Jobs.FirstOrDefault(job => job.Id == selectedId) ?? Jobs.FirstOrDefault();

            // Defer selection one dispatcher turn so ItemsControl finishes processing collection
            // changes before SelectedItem + settings form bind (avoids re-entrant layout crashes).
            TraceLoad($"reload: deferred select {next?.Id} gen={generation}");
            await _uiDispatcher.InvokeAsync(
                () =>
                {
                    if (cancellationToken.IsCancellationRequested
                        || (expectedSessionVersion is long version && !_workspaceSession.IsCurrent(version))
                        || generation != Volatile.Read(ref _reloadGeneration))
                    {
                        TraceLoad($"reload: deferred select dropped gen={generation}");
                        return;
                    }

                    // A user selection can change while this callback is queued. Do not restore
                    // the target captured before the fetch over that more recent choice.
                    var latest = _pinnedSelectedJobId is Guid latestId
                        ? Jobs.FirstOrDefault(job => job.Id == latestId) ?? Jobs.FirstOrDefault()
                        : next;
                    SelectedJob = latest;
                    if (latest is not null)
                    {
                        _pinnedSelectedJobId = latest.Id;
                    }
                },
                System.Windows.Threading.DispatcherPriority.Background);

            TraceLoad($"reload: end selected={SelectedJob?.Id} gen={generation}");
        }
        finally
        {
            if (generation == Volatile.Read(ref _reloadGeneration))
            {
                _isJobsReloadInProgress = false;
                NotifyListState();
                if (cancellationToken.IsCancellationRequested)
                {
                    StopElapsedTimer();
                }
                else
                {
                    SyncElapsedTimer();
                }
            }
        }
    }

    public void SyncElapsedTimer()
    {
        if (Jobs.Any(job => TrainingJobUi.IsActiveStatus(job.Status) && job.StartedAt is not null))
        {
            _elapsedTimer.Start();
            return;
        }

        _elapsedTimer.Stop();
    }

    public void StopElapsedTimer() => _elapsedTimer.Stop();

    public void CancelPendingPageWork()
    {
        _pageOperations.Cancel();
        _pageOperations.Dispose();
        _pageOperations = new CancellationTokenSource();
        Interlocked.Increment(ref _selectionGeneration);
        LoadCommand.Cancel();
        StopElapsedTimer();
    }

    private void OnElapsedTimerTick(object? sender, EventArgs e)
    {
        var hasActive = false;
        foreach (var job in Jobs)
        {
            if (!TrainingJobUi.IsActiveStatus(job.Status) || job.StartedAt is null)
            {
                continue;
            }

            hasActive = true;
            job.RefreshMetaText();
        }

        if (!hasActive)
        {
            _elapsedTimer.Stop();
            return;
        }

        ActiveJobElapsedTick?.Invoke();
    }

    public void SelectJobById(Guid jobId)
    {
        _pinnedSelectedJobId = jobId;
        SelectedJob = Jobs.FirstOrDefault(job => job.Id == jobId);
    }

    private async Task DeleteJobCoreAsync(TrainingJobListItem job)
    {
        var jobId = job.Id;
        var result = await _mediator.Send(new DeleteTrainingJobCommand(jobId));
        if (result.IsFailure)
        {
            // Keep list item on failure so the user can inspect the path and retry.
            _snackbar.ShowError(result.Error ?? "删除训练任务失败", "训练");
            return;
        }

        var index = Jobs.IndexOf(job);
        Jobs.Remove(job);
        RefreshJobMetrics();
        SelectedJob = Jobs.Count == 0
            ? null
            : Jobs[Math.Min(index, Jobs.Count - 1)];

        var value = result.Value;
        var message = value?.RunDirectoryDeleted == true
            ? "训练任务及运行数据已删除。"
            : value?.RunDirectoryAlreadyMissing == true
                ? "训练任务已删除（运行目录原本不存在）。"
                : "训练任务已删除。";
        _snackbar.ShowSuccess(message, "训练");
        JobDeleted?.Invoke(jobId);
    }

    public static string CreateDefaultJobName()
        => DateTime.Now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private string BuildUniqueJobName()
    {
        var defaultBase = CreateDefaultJobName();
        var existingNames = new HashSet<string>(
            Jobs.Select(job => job.Name),
            StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(defaultBase))
        {
            return defaultBase;
        }

        for (var index = 2; index < 10_000; index++)
        {
            var candidate = $"{defaultBase} ({index})";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }

        return CreateDefaultJobName();
    }

    private void RefreshJobMetrics()
    {
        JobCount = Jobs.Count;
    }

    partial void OnSelectedJobChanged(TrainingJobListItem? value)
    {
        var generation = Interlocked.Increment(ref _selectionGeneration);
        if (value is not null)
        {
            _pinnedSelectedJobId = value.Id;
        }

        // Hide detail panel BEFORE any form apply so NumberBox visual tree is not live
        // while properties thrash (root cause of hard exit at "selecting first job").
        IsDetailFormReady = false;
        NotifyListState();

        var handlers = SelectedJobChanged;
        if (handlers is null)
        {
            if (value is null)
            {
                return;
            }

            // No handlers registered yet — still allow detail once selection exists.
            IsDetailFormReady = true;
            NotifyListState();
            return;
        }

        _ = _uiDispatcher.InvokeAsync(
            () =>
            {
                if (generation == Volatile.Read(ref _selectionGeneration)
                    && ReferenceEquals(value, SelectedJob))
                {
                    SafeRaiseSelectedJobChanged(handlers, value);
                }
            },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    partial void OnIsDetailFormReadyChanged(bool value) => NotifyListState();

    private void SafeRaiseSelectedJobChanged(
        Action<TrainingJobListItem?> handlers,
        TrainingJobListItem? value)
    {
        try
        {
            TraceLoad($"select-handler: begin job={value?.Id} (detail hidden)");
            handlers.Invoke(value);
            // Reveal detail only after form + monitor state are applied.
            IsDetailFormReady = value is not null;
            NotifyListState();
            TraceLoad($"select-handler: end job={value?.Id} detailReady={IsDetailFormReady}");
        }
        catch (Exception ex)
        {
            IsDetailFormReady = false;
            NotifyListState();
            TraceLoad($"select-handler: failed {ex.GetType().Name}: {ex.Message}");
            _logger.LogError(ex, "SelectedJobChanged handlers failed for job {JobId}", value?.Id);
            _snackbar.ShowError($"加载任务详情失败：{ex.Message}", "训练");
        }
    }

    partial void OnHasProjectContextChanged(bool value) => NotifyListState();

    partial void OnJobCountChanged(int value) => NotifyListState();

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(HasJobs));
        OnPropertyChanged(nameof(HasEmptyJobList));
        OnPropertyChanged(nameof(ShowProjectRequired));
        OnPropertyChanged(nameof(HasSelectedJob));
        OnPropertyChanged(nameof(ShowSelectJobPrompt));
        OnPropertyChanged(nameof(ShowDetailPreparing));
        OnPropertyChanged(nameof(ShowJobDetail));
        OnPropertyChanged(nameof(IsTrainingWorkflow));
    }

    private void ApplyGalleriesOnUiThread(IReadOnlyList<GalleryOption> nextGalleries)
    {
        void Apply()
        {
            Galleries.Clear();
            foreach (var gallery in nextGalleries)
            {
                Galleries.Add(gallery);
            }
        }

        _uiDispatcher.Invoke(Apply);
    }

    private Task WaitForUiIdleAsync()
        => _uiDispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
}
