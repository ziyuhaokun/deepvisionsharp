using System.Buffers;
using OpenCvSharp;
using TorchSharp;
using static TorchSharp.torch;
using DeepVisionSharp.Tiling;
using DeepVisionSharp.TorchSharp.Detection.Yolo26;
using DeepVisionSharp.TorchSharp.Models;
using DeepVisionSharp.TorchSharp.Tensors;
using CvScalar = OpenCvSharp.Scalar;
using CvSize = OpenCvSharp.Size;

namespace DeepVisionSharp.TorchSharp.Detection;

/// <summary>
/// Letterbox geometry for detection: source pixels, canvas pixels and back.
/// The fit itself comes from <see cref="ImageFits"/>; this type only applies it to pixels and boxes.
/// </summary>
public sealed class DetectionLetterbox
{
    private const byte FillValue = 114;

    private DetectionLetterbox(ImageFit fit)
    {
        Fit = fit;
    }

    public ImageFit Fit { get; }

    public int CanvasWidth => Fit.Canvas.Width;

    public int CanvasHeight => Fit.Canvas.Height;

    public static DetectionLetterbox For(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceHeight, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetHeight, 1);
        return new DetectionLetterbox(ImageFits.Letterbox(
            new PixelSize(sourceWidth, sourceHeight),
            new PixelSize(targetWidth, targetHeight)));
    }

    /// <summary>Draws the source into a padded canvas. Caller owns the returned Mat.</summary>
    public Mat Apply(Mat source)
    {
        var channels = source.Channels();
        var canvas = new Mat(
            CanvasHeight,
            CanvasWidth,
            MatType.CV_8UC(channels),
            new CvScalar(FillValue, FillValue, FillValue, FillValue));
        try
        {
            var dest = Fit.DestRect;
            using var resized = new Mat();
            Cv2.Resize(source, resized, new CvSize(dest.Width, dest.Height), 0, 0, InterpolationFlags.Linear);
            using var roi = new Mat(canvas, new Rect(dest.X, dest.Y, dest.Width, dest.Height));
            resized.CopyTo(roi);
            return canvas;
        }
        catch
        {
            canvas.Dispose();
            throw;
        }
    }

    /// <summary>Maps a source-pixel ground-truth box into canvas pixels; returns null when it vanishes.</summary>
    public GroundTruthBox? Transform(GroundTruthBox box)
    {
        var x = Fit.DestRect.X + ((box.X - Fit.SourceRect.X) * Fit.ScaleX);
        var y = Fit.DestRect.Y + ((box.Y - Fit.SourceRect.Y) * Fit.ScaleY);
        var width = box.Width * Fit.ScaleX;
        var height = box.Height * Fit.ScaleY;
        var left = (float)Math.Clamp(x, 0, CanvasWidth);
        var top = (float)Math.Clamp(y, 0, CanvasHeight);
        var right = (float)Math.Clamp(x + width, 0, CanvasWidth);
        var bottom = (float)Math.Clamp(y + height, 0, CanvasHeight);
        if (right - left < 1 || bottom - top < 1)
        {
            return null;
        }

        return new GroundTruthBox(box.ClassIndex, left, top, right - left, bottom - top);
    }

    /// <summary>Maps a canvas-pixel box back to the original image; null when entirely in the padding.</summary>
    public ScoredDetection? ToSource(ScoredDetection detection)
    {
        var x = Fit.SourceRect.X + ((detection.X - Fit.DestRect.X) / Fit.ScaleX);
        var y = Fit.SourceRect.Y + ((detection.Y - Fit.DestRect.Y) / Fit.ScaleY);
        var width = detection.Width / Fit.ScaleX;
        var height = detection.Height / Fit.ScaleY;
        var left = (float)Math.Clamp(x, 0, Fit.Source.Width);
        var top = (float)Math.Clamp(y, 0, Fit.Source.Height);
        var right = (float)Math.Clamp(x + width, 0, Fit.Source.Width);
        var bottom = (float)Math.Clamp(y + height, 0, Fit.Source.Height);
        if (right - left < 0.5f || bottom - top < 0.5f)
        {
            return null;
        }

        return detection with { X = left, Y = top, Width = right - left, Height = bottom - top };
    }

    /// <summary>Converts a padded BGR/gray canvas to a (1, C, H, W) float tensor.</summary>
    public static Tensor ToTensor(Mat canvas, torch.Device device, PreprocessingVersion preprocessing)
    {
        var channels = canvas.Channels();
        var height = canvas.Height;
        var width = canvas.Width;
        var total = channels * height * width;
        var rented = ArrayPool<float>.Shared.Rent(total);
        try
        {
            TorchSharpImageTensor.FillNormalizedData(canvas, rented.AsSpan(0, total), preprocessing);
            return torch.tensor(
                rented.AsMemory(0, total),
                [1, channels, height, width],
                dtype: torch.ScalarType.Float32,
                device: device);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }
}
