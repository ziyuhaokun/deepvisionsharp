using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DeepVisionSharp.Shell.Avalonia.Controls;

/// <summary>后台任务状态配色：参数 dot / badge / text；输入为 [IsCancelled, IsRunning, IsSucceeded, IsFailed]。</summary>
public sealed class TaskStatusBrushConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool Flag(int index) => values.Count > index && values[index] is true;

        var isCancelled = Flag(0);
        var isRunning = Flag(1);
        var isSucceeded = Flag(2);
        var isFailed = Flag(3);
        var target = parameter as string;

        var key = isRunning
            ? target switch { "dot" => "IvAccentBrush", "badge" => "IvAccentSubtleBrush", _ => "IvAccentTextBrush" }
            : isSucceeded
                ? target switch { "dot" => "IvSuccessBrush", "badge" => "IvSuccessSubtleBrush", _ => "IvSuccessTextBrush" }
                : isFailed
                    ? target switch { "dot" => "IvDangerBrush", "badge" => "IvDangerSubtleBrush", _ => "IvDangerTextBrush" }
                    : target switch { "dot" => "IvText3Brush", "badge" => "IvControlFillBrush", _ => "IvText2Brush" };

        if (isCancelled)
        {
            key = target switch { "dot" => "IvText3Brush", "badge" => "IvControlFillBrush", _ => "IvText2Brush" };
        }

        if (global::Avalonia.Application.Current?.Resources.TryGetResource(key, null, out var resource) == true && resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.Transparent;
    }
}