namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class TrainingEnvironmentCheckCoordinator
{
    private readonly IReadOnlyList<ITrainingEnvironmentChecker> _checkers;

    public TrainingEnvironmentCheckCoordinator(IEnumerable<ITrainingEnvironmentChecker> checkers)
    {
        _checkers = checkers.ToArray();
    }

    public async Task<TrainingEnvironmentCheckResult> CheckAsync(
        TrainingEnvironmentCheckContext context,
        CancellationToken cancellationToken)
    {
        var matching = _checkers.Where(checker => checker.CanHandle(context)).ToArray();
        if (matching.Length == 0)
        {
            // Hard-fail: silent Passed hid unregistered backends and skipped real preflight (H4).
            var backendKey = string.IsNullOrWhiteSpace(context.Backend.BackendKey)
                ? "(empty)"
                : context.Backend.BackendKey.Trim();
            return new TrainingEnvironmentCheckResult(
                [
                    new TrainingEnvironmentIssue(
                        "ENV_CHECKER_MISSING",
                        $"未注册训练环境检查器：后端 {backendKey}",
                        "请确认该模型类型已配置环境预检，或升级产品。")
                ],
                [],
                []);
        }

        var errors = new List<TrainingEnvironmentIssue>();
        var warnings = new List<TrainingEnvironmentIssue>();
        var notes = new List<TrainingEnvironmentIssue>();

        foreach (var checker in matching)
        {
            var result = await checker.CheckAsync(context, cancellationToken).ConfigureAwait(false);
            errors.AddRange(result.Errors);
            warnings.AddRange(result.Warnings);
            notes.AddRange(result.Notes);
        }

        return new TrainingEnvironmentCheckResult(errors, warnings, notes);
    }
}
