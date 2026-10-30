using System.Globalization;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Domain.Datasets;

/// <summary>One YOLO label line: class index plus a normalized box (cx cy w h in 0..1).</summary>
public readonly record struct YoloBox(
    int ClassIndex,
    double CenterX,
    double CenterY,
    double Width,
    double Height);

/// <summary>
/// YOLO dataset text formats shared by import and export. Kept in Application so the UI,
/// the importer and the exporter all agree on one parser and one formatter.
/// </summary>
public static class YoloDatasetFormat
{
    /// <summary>
    /// Reads class names from a YOLO <c>data.yaml</c>/<c>dataset.yaml</c> <c>names</c> block or a
    /// plain <c>classes.txt</c> (one name per line). The parser is deliberately tolerant: it accepts
    /// inline lists, <c>- name</c> items and <c>index: name</c> mappings without a YAML dependency.
    /// </summary>
    public static Result<IReadOnlyList<string>> ParseClasses(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result<IReadOnlyList<string>>.Failure("类别文件为空。");
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var names = new List<string>();
        var namesLine = Array.FindIndex(lines, line => line.TrimStart().StartsWith("names:", StringComparison.OrdinalIgnoreCase));
        if (namesLine >= 0)
        {
            var inline = lines[namesLine].TrimStart()["names:".Length..].Trim();
            if (inline.StartsWith('['))
            {
                var end = inline.LastIndexOf(']');
                var body = end > 0 ? inline[1..end] : inline[1..];
                names.AddRange(body.Split(',').Select(CleanName));
            }
            else if (!string.IsNullOrWhiteSpace(inline))
            {
                names.Add(CleanName(inline));
            }
            else
            {
                for (var index = namesLine + 1; index < lines.Length; index++)
                {
                    var line = lines[index];
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    {
                        continue;
                    }

                    // A new top-level key (nc:, train:, ...) ends the names block.
                    if (!char.IsWhiteSpace(line[0]) && trimmed.Contains(':'))
                    {
                        break;
                    }

                    if (trimmed.StartsWith('-'))
                    {
                        names.Add(CleanName(trimmed[1..]));
                        continue;
                    }

                    var colon = trimmed.IndexOf(':');
                    if (colon > 0 && int.TryParse(trimmed[..colon].Trim(), out _))
                    {
                        names.Add(CleanName(trimmed[(colon + 1)..]));
                        continue;
                    }

                    names.Add(CleanName(trimmed));
                }
            }
        }
        else
        {
            names.AddRange(lines
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .Select(CleanName));
        }

        var effective = names.Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        return effective.Length > 0
            ? Result<IReadOnlyList<string>>.Success(effective)
            : Result<IReadOnlyList<string>>.Failure("类别文件未解析出任何类别名。");
    }

    /// <summary>
    /// Parses label lines. Malformed lines are reported through <paramref name="warnings"/> and
    /// skipped so one bad row cannot make a whole dataset unusable.
    /// </summary>
    public static IReadOnlyList<YoloBox> ParseLabels(
        string? text,
        int classCount,
        out IReadOnlyList<string> warnings)
    {
        var boxes = new List<YoloBox>();
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            warnings = problems;
            return boxes;
        }

        var lineNumber = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var classIndex)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cx)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cy)
                || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                || !double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
            {
                problems.Add($"第 {lineNumber} 行格式无效：{line}");
                continue;
            }

            if (classIndex < 0 || classIndex >= classCount)
            {
                problems.Add($"第 {lineNumber} 行类别索引 {classIndex} 超出范围 0..{classCount - 1}");
                continue;
            }

            if (width <= 0 || height <= 0)
            {
                problems.Add($"第 {lineNumber} 行宽高必须为正数");
                continue;
            }

            boxes.Add(new YoloBox(
                classIndex,
                Math.Clamp(cx, 0, 1),
                Math.Clamp(cy, 0, 1),
                Math.Clamp(width, 0, 1),
                Math.Clamp(height, 0, 1)));
        }

        warnings = problems;
        return boxes;
    }

    /// <summary>Converts a normalized YOLO box to a pixel-space shape; null when it has no area.</summary>
    public static AnnotationShape? ToShape(
        YoloBox box,
        string label,
        double imageWidth,
        double imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var shape = new AnnotationShape(
            label.Trim(),
            AnnotationShapeKind.Rectangle,
            (box.CenterX - (box.Width / 2)) * imageWidth,
            (box.CenterY - (box.Height / 2)) * imageHeight,
            box.Width * imageWidth,
            box.Height * imageHeight);
        return shape.ClampTo(imageWidth, imageHeight);
    }

    /// <summary>Formats pixel-space shapes as YOLO label lines.</summary>
    public static string FormatLabels(
        IEnumerable<(int ClassIndex, AnnotationShape Shape)> shapes,
        double imageWidth,
        double imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        foreach (var (classIndex, shape) in shapes)
        {
            var clamped = shape.ClampTo(imageWidth, imageHeight);
            if (clamped is null)
            {
                continue;
            }

            var cx = (clamped.X + (clamped.Width / 2)) / imageWidth;
            var cy = (clamped.Y + (clamped.Height / 2)) / imageHeight;
            var width = clamped.Width / imageWidth;
            var height = clamped.Height / imageHeight;
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{classIndex} {cx:0.######} {cy:0.######} {width:0.######} {height:0.######}"));
        }

        return lines.Count == 0 ? string.Empty : string.Join('\n', lines) + "\n";
    }

    private static string CleanName(string value) =>
        value.Trim().Trim(',').Trim().Trim('"').Trim('\'').Trim();
}
