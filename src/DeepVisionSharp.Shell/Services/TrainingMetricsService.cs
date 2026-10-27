using System.Text.Json;

using DeepVisionSharp.Shell.ViewModels;

namespace DeepVisionSharp.Shell.Services;

public sealed class TrainingMetricsService
{
    public bool TryParseMetricLine(string line, out TrainingMetricPoint point)
    {
        point = null!;

        const string prefix = "DV_METRICS ";
        var idx = line.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0)
        {
            return false;
        }

        var json = line[(idx + prefix.Length)..].Trim();
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("epoch", out var epochEl) || !epochEl.TryGetInt32(out var epoch) || epoch <= 0)
            {
                return false;
            }

            var phase = root.TryGetProperty("phase", out var phaseEl) ? phaseEl.GetString() ?? string.Empty : string.Empty;
            var isTrain = string.Equals(phase, "train", StringComparison.OrdinalIgnoreCase);
            var isValidation = string.Equals(phase, "val", StringComparison.OrdinalIgnoreCase);
            var loss = ReadNumber(root, "loss");
            var trainLossExplicit = ReadNumber(root, "train_loss");
            var valLossExplicit = ReadNumber(root, "val_loss");
            var boxLoss = ReadNumber(root, "box_loss");
            var clsLoss = ReadNumber(root, "cls_loss");
            var dflLoss = ReadNumber(root, "dfl_loss");

            var accuracyTop1 = ReadNumber(root, "accuracy_top1");
            var accuracyTop5 = ReadNumber(root, "accuracy_top5");
            var map50 = ReadNumber(root, "map_50");
            var map = ReadNumber(root, "map");
            var maskMap50 = ReadNumber(root, "mask_map_50");
            var maskMap = ReadNumber(root, "mask_map_50_95");
            var boxMap50 = ReadNumber(root, "box_map_50");
            var boxMap = ReadNumber(root, "box_map_50_95");
            var miou = ReadNumber(root, "miou");
            var dice = ReadNumber(root, "dice");
            var pixelAcc = ReadNumber(root, "pixel_acc") ?? ReadNumber(root, "pixel_accuracy");
            var perClass = ParsePerClass(root);

            // Classification logs historically aliased Top-1 into map_50 only.
            // Instance-seg emits box_*/mask_*; semantic emits miou/dice.
            var hasInstanceSegMaps = maskMap50 is not null
                || maskMap is not null
                || boxMap50 is not null
                || boxMap is not null;
            var hasSemanticMaps = miou is not null || dice is not null || pixelAcc is not null;
            if (!hasInstanceSegMaps && !hasSemanticMaps)
            {
                if (accuracyTop1 is null && map50 is not null && isValidation)
                {
                    accuracyTop1 = map50;
                }

                if (map50 is null && accuracyTop1 is not null)
                {
                    map50 = accuracyTop1;
                }
            }
            else if (isValidation)
            {
                // Detection-compatible aliases for charts that still read ValMap/ValMap50.
                if (hasInstanceSegMaps)
                {
                    map50 ??= maskMap50 ?? boxMap50;
                    map ??= maskMap ?? boxMap;
                }
                else if (hasSemanticMaps)
                {
                    map50 ??= miou;
                    map ??= dice;
                }
            }

            point = new TrainingMetricPoint
            {
                Epoch = epoch,
                TrainLoss = trainLossExplicit ?? (isTrain ? loss : null),
                TrainBoxLoss = isTrain ? boxLoss : null,
                TrainClsLoss = isTrain ? clsLoss : null,
                TrainDflLoss = isTrain ? dflLoss : null,
                ValLoss = valLossExplicit ?? (isValidation && trainLossExplicit is null ? loss : null),
                ValBoxLoss = isValidation ? boxLoss : null,
                ValClsLoss = isValidation ? clsLoss : null,
                ValDflLoss = isValidation ? dflLoss : null,
                Lr = ReadNumber(root, "lr"),
                ValMap = isValidation ? map : null,
                ValMap50 = isValidation ? map50 : null,
                MaskMap = isValidation ? maskMap : null,
                MaskMap50 = isValidation ? maskMap50 : null,
                BoxMap = isValidation ? boxMap : null,
                BoxMap50 = isValidation ? boxMap50 : null,
                Miou = isValidation ? miou : null,
                Dice = isValidation ? dice : null,
                PixelAccuracy = isValidation ? pixelAcc : null,
                AccuracyTop1 = isValidation && !hasInstanceSegMaps && !hasSemanticMaps ? accuracyTop1 : null,
                AccuracyTop5 = isValidation && !hasInstanceSegMaps && !hasSemanticMaps ? accuracyTop5 : null,
                PerClass = isValidation ? perClass : null,
                Precision = isValidation && !hasInstanceSegMaps && !hasSemanticMaps
                    ? ReadNumber(root, "precision")
                    : null,
                Recall = isValidation && !hasInstanceSegMaps && !hasSemanticMaps
                    ? ReadNumber(root, "recall")
                    : null
            };

            // Classification emits a single val-phase line that may also carry train_loss/lr.
            if (isValidation && trainLossExplicit is not null)
            {
                point.TrainLoss = trainLossExplicit;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<TrainingPerClassPoint>? ParsePerClass(JsonElement root)
    {
        if (!root.TryGetProperty("per_class", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var metrics = new List<TrainingPerClassPoint>();
        foreach (var item in array.EnumerateArray())
        {
            var name = item.TryGetProperty("class", out var nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            metrics.Add(new TrainingPerClassPoint(
                name!,
                ReadNumber(item, "precision") ?? 0,
                ReadNumber(item, "recall") ?? 0,
                ReadNumber(item, "f1") ?? 0,
                (int)(ReadNumber(item, "support") ?? 0)));
        }

        return metrics.Count > 0 ? metrics : null;
    }

    private static double? ReadNumber(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.Number
            ? element.GetDouble()
            : null;

    public static bool UpsertMetric(List<TrainingMetricPoint> metrics, TrainingMetricPoint update)
    {
        var index = metrics.FindIndex(point => point.Epoch == update.Epoch);
        if (index < 0)
        {
            metrics.Add(update);
            return true;
        }

        var existing = metrics[index];
        var changed = false;

        if (update.TrainLoss is not null && existing.TrainLoss != update.TrainLoss)
        {
            existing.TrainLoss = update.TrainLoss;
            changed = true;
        }

        if (update.TrainBoxLoss is not null && existing.TrainBoxLoss != update.TrainBoxLoss)
        {
            existing.TrainBoxLoss = update.TrainBoxLoss;
            changed = true;
        }

        if (update.TrainClsLoss is not null && existing.TrainClsLoss != update.TrainClsLoss)
        {
            existing.TrainClsLoss = update.TrainClsLoss;
            changed = true;
        }

        if (update.TrainDflLoss is not null && existing.TrainDflLoss != update.TrainDflLoss)
        {
            existing.TrainDflLoss = update.TrainDflLoss;
            changed = true;
        }

        if (update.ValLoss is not null && existing.ValLoss != update.ValLoss)
        {
            existing.ValLoss = update.ValLoss;
            changed = true;
        }

        if (update.ValBoxLoss is not null && existing.ValBoxLoss != update.ValBoxLoss)
        {
            existing.ValBoxLoss = update.ValBoxLoss;
            changed = true;
        }

        if (update.ValClsLoss is not null && existing.ValClsLoss != update.ValClsLoss)
        {
            existing.ValClsLoss = update.ValClsLoss;
            changed = true;
        }

        if (update.ValDflLoss is not null && existing.ValDflLoss != update.ValDflLoss)
        {
            existing.ValDflLoss = update.ValDflLoss;
            changed = true;
        }

        if (update.Lr is not null && existing.Lr != update.Lr)
        {
            existing.Lr = update.Lr;
            changed = true;
        }

        if (update.ValMap is not null && existing.ValMap != update.ValMap)
        {
            existing.ValMap = update.ValMap;
            changed = true;
        }

        if (update.ValMap50 is not null && existing.ValMap50 != update.ValMap50)
        {
            existing.ValMap50 = update.ValMap50;
            changed = true;
        }

        if (update.MaskMap is not null && existing.MaskMap != update.MaskMap)
        {
            existing.MaskMap = update.MaskMap;
            changed = true;
        }

        if (update.MaskMap50 is not null && existing.MaskMap50 != update.MaskMap50)
        {
            existing.MaskMap50 = update.MaskMap50;
            changed = true;
        }

        if (update.BoxMap is not null && existing.BoxMap != update.BoxMap)
        {
            existing.BoxMap = update.BoxMap;
            changed = true;
        }

        if (update.BoxMap50 is not null && existing.BoxMap50 != update.BoxMap50)
        {
            existing.BoxMap50 = update.BoxMap50;
            changed = true;
        }

        if (update.Miou is not null && existing.Miou != update.Miou)
        {
            existing.Miou = update.Miou;
            changed = true;
        }

        if (update.Dice is not null && existing.Dice != update.Dice)
        {
            existing.Dice = update.Dice;
            changed = true;
        }

        if (update.PixelAccuracy is not null && existing.PixelAccuracy != update.PixelAccuracy)
        {
            existing.PixelAccuracy = update.PixelAccuracy;
            changed = true;
        }

        if (update.AccuracyTop1 is not null && existing.AccuracyTop1 != update.AccuracyTop1)
        {
            existing.AccuracyTop1 = update.AccuracyTop1;
            changed = true;
        }

        if (update.AccuracyTop5 is not null && existing.AccuracyTop5 != update.AccuracyTop5)
        {
            existing.AccuracyTop5 = update.AccuracyTop5;
            changed = true;
        }

        if (update.Precision is not null && existing.Precision != update.Precision)
        {
            existing.Precision = update.Precision;
            changed = true;
        }

        if (update.Recall is not null && existing.Recall != update.Recall)
        {
            existing.Recall = update.Recall;
            changed = true;
        }

        if (update.PerClass is { Count: > 0 })
        {
            existing.PerClass = update.PerClass;
            changed = true;
        }

        // Keep map_50 alias in sync with Top-1 for CLS rows that only updated accuracy.
        // Skip when segmentation mask/box maps are present so Mask mAP remains the primary ranking key.
        var hasSegmentationMaps = existing.MaskMap50 is not null
            || existing.MaskMap is not null
            || existing.BoxMap50 is not null
            || existing.BoxMap is not null;
        if (!hasSegmentationMaps)
        {
            if (existing.AccuracyTop1 is not null && existing.ValMap50 is null)
            {
                existing.ValMap50 = existing.AccuracyTop1;
                changed = true;
            }

            if (existing.ValMap50 is not null && existing.AccuracyTop1 is null)
            {
                existing.AccuracyTop1 = existing.ValMap50;
                changed = true;
            }
        }

        return changed;
    }
}
