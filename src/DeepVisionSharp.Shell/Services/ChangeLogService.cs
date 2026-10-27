using System.IO;
using System.Text;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// 加载并解析仓库根目录用户版 <c>CHANGELOG.md</c>（Markdown：<c>### vX.Y.Z (日期)</c> + <c>-</c> 条目）。
/// 技术细节见 <c>CHANGELOG.Developer.md</c>（不复制到输出、不由此服务加载）。
/// </summary>
public sealed class ChangeLogService
{
    /// <summary>用户版变更日志文件名（设置页「关于」唯一数据源）。</summary>
    private const string FileName = "CHANGELOG.md";

    public IReadOnlyList<ChangeLogEntry> Entries { get; }

    public ChangeLogEntry? CurrentEntry { get; }

    public IReadOnlyList<ChangeLogEntry> HistoricalEntries { get; }

    public string? SourcePath { get; }

    public bool HasEntries => Entries.Count > 0;

    public ChangeLogService()
    {
        var path = ResolveChangeLogPath();
        SourcePath = path;
        if (path is null || !File.Exists(path))
        {
            Entries = Array.Empty<ChangeLogEntry>();
            CurrentEntry = null;
            HistoricalEntries = Array.Empty<ChangeLogEntry>();
            return;
        }

        var content = File.ReadAllText(path, Encoding.UTF8);
        Entries = ParseChangeLog(content);
        CurrentEntry = Entries.FirstOrDefault(entry => entry.IsLatest) ?? Entries.FirstOrDefault();
        HistoricalEntries = Entries.Where(entry => !entry.IsLatest).ToArray();
    }

    private static string? ResolveChangeLogPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            // Published / bin output (copied by Shell.csproj)
            Path.Combine(baseDir, FileName),
            // Dev fallback: repo root from .../Shell/bin/{Config}/{TFM}/
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", FileName)),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static IReadOnlyList<ChangeLogEntry> ParseChangeLog(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<ChangeLogEntry>();
        }

        var markdown = ParseMarkdownChangeLog(content);
        return markdown.Count > 0 ? markdown : ParseStructuredChangeLog(content);
    }

    /// <summary>
    /// 现行格式：<c>### vX.Y.Z (发布日期)</c>、可选 <c>tags:</c>、随后 <c>-</c> 条目。
    /// </summary>
    private static List<ChangeLogEntry> ParseMarkdownChangeLog(string content)
    {
        var entries = new List<ChangeLogEntry>();
        string? version = null;
        string? releasedOn = null;
        string? tags = null;
        var items = new List<string>();

        void Flush()
        {
            if (string.IsNullOrWhiteSpace(version) || items.Count == 0)
            {
                version = null;
                releasedOn = null;
                tags = null;
                items = new List<string>();
                return;
            }

            entries.Add(new ChangeLogEntry(version, releasedOn, tags, items.ToArray()));
            version = null;
            releasedOn = null;
            tags = null;
            items = new List<string>();
        }

        foreach (var rawLine in EnumerateLines(content))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(">", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                Flush();
                var header = line[4..].Trim();
                // e.g. "v1.4.0 (2026-07-14)" or "v1.4.0"
                var openParen = header.IndexOf('(', StringComparison.Ordinal);
                if (openParen > 0)
                {
                    version = header[..openParen].Trim();
                    var closeParen = header.IndexOf(')', openParen + 1);
                    releasedOn = closeParen > openParen
                        ? header[(openParen + 1)..closeParen].Trim()
                        : null;
                }
                else
                {
                    version = header;
                }

                continue;
            }

            if (version is null)
            {
                continue;
            }

            if (TryParseField(line, out var key, out var value) && key == "tags")
            {
                tags = value;
                continue;
            }

            if (IsListItem(line))
            {
                AddIfNotEmpty(items, NormalizeListItem(line));
            }
        }

        Flush();
        MarkFirstAsLatest(entries);
        return entries;
    }

    /// <summary>旧 Vision.Master <c>@entry</c> 节点，仅作历史文件兼容。</summary>
    private static List<ChangeLogEntry> ParseStructuredChangeLog(string content)
    {
        var entries = new List<ChangeLogEntry>();
        StructuredBuilder? builder = null;

        foreach (var rawLine in EnumerateLines(content))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Equals("@entry", StringComparison.OrdinalIgnoreCase))
            {
                builder = new StructuredBuilder();
                continue;
            }

            if (line.Equals("@end", StringComparison.OrdinalIgnoreCase))
            {
                if (builder is not null && builder.TryBuild(out var entry) && entry is not null)
                {
                    entries.Add(entry);
                }

                builder = null;
                continue;
            }

            if (builder is null)
            {
                continue;
            }

            if (IsListItem(line))
            {
                AddIfNotEmpty(builder.Items, NormalizeListItem(line));
                continue;
            }

            if (!TryParseField(line, out var key, out var value))
            {
                AddIfNotEmpty(builder.Items, NormalizeListItem(line));
                continue;
            }

            switch (key)
            {
                case "version":
                    builder.Version = value;
                    break;
                case "released":
                    builder.ReleasedOn = value;
                    break;
                case "tags":
                    builder.Tags = value;
                    break;
                case "item":
                    AddIfNotEmpty(builder.Items, value);
                    break;
                default:
                    AddIfNotEmpty(builder.Items, NormalizeListItem(line));
                    break;
            }
        }

        MarkFirstAsLatest(entries);
        return entries;
    }

    private static void MarkFirstAsLatest(List<ChangeLogEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var first = entries[0];
        entries[0] = first with { IsLatest = true };
    }

    private static IEnumerable<string> EnumerateLines(string content)
    {
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static bool TryParseField(string line, out string key, out string value)
    {
        var separatorIndex = line.IndexOf(':');
        if (separatorIndex <= 0)
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = line[..separatorIndex].Trim().ToLowerInvariant();
        value = line[(separatorIndex + 1)..].Trim();
        return key.Length > 0;
    }

    private static bool IsListItem(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            return true;
        }

        var index = 0;
        while (index < trimmed.Length && char.IsDigit(trimmed[index]))
        {
            index++;
        }

        return index > 0 && index < trimmed.Length && trimmed[index] is '.' or '、' or ')' or '）';
    }

    private static string NormalizeListItem(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            return trimmed[2..].Trim();
        }

        var index = 0;
        while (index < trimmed.Length && char.IsDigit(trimmed[index]))
        {
            index++;
        }

        if (index > 0 && index < trimmed.Length && trimmed[index] is '.' or '、' or ')' or '）')
        {
            return trimmed[(index + 1)..].Trim();
        }

        return trimmed;
    }

    private static void AddIfNotEmpty(ICollection<string> items, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            items.Add(value.Trim());
        }
    }

    private sealed class StructuredBuilder
    {
        public string? Version { get; set; }

        public string? ReleasedOn { get; set; }

        public string? Tags { get; set; }

        public List<string> Items { get; } = new();

        public bool TryBuild(out ChangeLogEntry? entry)
        {
            if (string.IsNullOrWhiteSpace(Version) || Items.Count == 0)
            {
                entry = null;
                return false;
            }

            entry = new ChangeLogEntry(Version.Trim(), ReleasedOn, Tags, Items.ToArray());
            return true;
        }
    }
}
