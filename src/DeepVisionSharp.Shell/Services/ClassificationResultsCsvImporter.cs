using System.Globalization;
using System.IO;
using System.Text;

using DeepVisionSharp.Shell.ViewModels;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// Merges a classification <c>results.csv</c> into live metric series
/// so charts remain rich even when DV_METRICS lines are sparse.
/// </summary>
public static class ClassificationResultsCsvImporter
{
    private static readonly string[] PreferredRelativePaths =
    [
        Path.Combine("plots", "results.csv"),
        Path.Combine("train", "results.csv"),
        "results.csv"
    ];

    public static int MergeInto(string? outputRoot, List<TrainingMetricPoint> metrics)
    {
        if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot))
        {
            return 0;
        }

        var csvPath = PreferredRelativePaths
            .Select(relative => Path.Combine(outputRoot, relative))
            .FirstOrDefault(File.Exists);
        if (csvPath is null)
        {
            // Fallback: search one level deep.
            try
            {
                csvPath = Directory.EnumerateFiles(outputRoot, "results.csv", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            }
            catch
            {
                return 0;
            }
        }

        if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
        {
            return 0;
        }

        try
        {
            var lines = File.ReadAllLines(csvPath, Encoding.UTF8);
            if (lines.Length < 2)
            {
                return 0;
            }

            var headers = SplitCsvLine(lines[0])
                .Select(header => header.Trim())
                .ToArray();
            if (headers.Length == 0)
            {
                return 0;
            }

            var changed = 0;
            for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                var cells = SplitCsvLine(lines[lineIndex]);
                if (cells.Length == 0)
                {
                    continue;
                }

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < headers.Length && i < cells.Length; i++)
                {
                    map[headers[i]] = cells[i].Trim();
                }

                var epoch = ReadInt(map, "epoch") ?? lineIndex;
                if (epoch <= 0)
                {
                    continue;
                }

                var update = new TrainingMetricPoint
                {
                    Epoch = epoch,
                    TrainLoss = ReadDouble(map, "train/loss", "train_loss"),
                    ValLoss = ReadDouble(map, "val/loss", "metrics/loss"),
                    AccuracyTop1 = ReadDouble(map, "metrics/accuracy_top1", "accuracy_top1"),
                    AccuracyTop5 = ReadDouble(map, "metrics/accuracy_top5", "accuracy_top5"),
                    Lr = ReadDouble(map, "lr/pg0", "lr/0", "lr"),
                };

                if (update.AccuracyTop1 is not null)
                {
                    update.ValMap50 = update.AccuracyTop1;
                }

                if (TrainingMetricsService.UpsertMetric(metrics, update))
                {
                    changed++;
                }
            }

            return changed;
        }
        catch
        {
            return 0;
        }
    }

    public static IReadOnlyList<string> FindPlotImagePaths(string? outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot))
        {
            return Array.Empty<string>();
        }

        var preferredNames = new[]
        {
            "cls_summary_curves.png",
            "results.png",
            "confusion_matrix_normalized.png",
            "confusion_matrix.png",
            "train_batch0.jpg",
            "val_batch0_pred.jpg",
            "val_batch0_labels.jpg",
        };

        var plotsDir = Path.Combine(outputRoot, "plots");
        var trainDir = Path.Combine(outputRoot, "train");
        var roots = new[] { plotsDir, trainDir, outputRoot }
            .Where(Directory.Exists)
            .ToArray();

        var found = new List<string>();
        foreach (var name in preferredNames)
        {
            foreach (var root in roots)
            {
                var direct = Path.Combine(root, name);
                if (File.Exists(direct))
                {
                    found.Add(direct);
                    break;
                }

                try
                {
                    var nested = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        found.Add(nested);
                        break;
                    }
                }
                catch
                {
                    // ignore enumeration errors
                }
            }
        }

        return found
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string? ResolvePlotsDirectory(string? outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return null;
        }

        var plots = Path.Combine(outputRoot, "plots");
        if (Directory.Exists(plots))
        {
            return plots;
        }

        var train = Path.Combine(outputRoot, "train");
        return Directory.Exists(train) ? train : (Directory.Exists(outputRoot) ? outputRoot : null);
    }

    private static string[] SplitCsvLine(string line)
    {
        // results.csv is simple comma-separated without nested quotes for metric columns.
        return line.Split(',');
    }

    private static int? ReadInt(IReadOnlyDictionary<string, string> map, params string[] keys)
    {
        var value = ReadDouble(map, keys);
        if (value is null)
        {
            return null;
        }

        return (int)Math.Round(value.Value);
    }

    private static double? ReadDouble(IReadOnlyDictionary<string, string> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                // Also try with whitespace-normalized keys already trimmed in map.
                continue;
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && double.IsFinite(number))
            {
                return number;
            }
        }

        // Fuzzy: header may contain surrounding spaces already trimmed; try ends-with match.
        foreach (var key in keys)
        {
            var match = map.FirstOrDefault(pair =>
                pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                || pair.Key.EndsWith(key, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(match.Value))
            {
                continue;
            }

            if (double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && double.IsFinite(number))
            {
                return number;
            }
        }

        return null;
    }
}
