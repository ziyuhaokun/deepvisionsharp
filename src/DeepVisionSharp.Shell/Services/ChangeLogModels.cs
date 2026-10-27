namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// 产品变更节点（来源为用户版 CHANGELOG.md 的 Markdown 版本节）。
/// </summary>
public sealed record ChangeLogEntry(
    string Version,
    string? ReleasedOn,
    string? Tags,
    IReadOnlyList<string> Items,
    bool IsLatest = false)
{
    public string Title => string.IsNullOrWhiteSpace(ReleasedOn)
        ? Version
        : $"{Version}  ·  {ReleasedOn}";

    public string VersionBadge =>
        Version.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? Version
            : $"v{Version}";

    public string ItemsSummary => Items.Count == 0
        ? "无变更条目"
        : string.Join(Environment.NewLine, Items.Select((item, index) => $"{index + 1}. {item}"));
}
