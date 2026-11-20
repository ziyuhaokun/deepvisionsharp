namespace DeepVisionSharp.Shell.Avalonia.Controls.Labeling;

/// <summary>
/// 画布浮层（角标 / 视图工具条）的置顶状态，按 PinId 记在进程内。
/// deepvision 里这份状态走 Shell 的配置存储；本波没有配置服务，先按会话内存存，语义仍是「同一位置只置顶一次」。
/// </summary>
internal static class CanvasFloatPins
{
    private static readonly Dictionary<string, bool> Pins = new(StringComparer.Ordinal);

    public static bool GetPin(string pinId) => Pins.TryGetValue(pinId, out var pinned) && pinned;

    public static void SetPin(string pinId, bool pinned) => Pins[pinId] = pinned;
}
