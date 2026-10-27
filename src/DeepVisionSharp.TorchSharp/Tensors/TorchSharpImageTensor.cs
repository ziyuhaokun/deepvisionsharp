using OpenCvSharp;
using TorchSharp;
using DeepVisionSharp.TorchSharp.Models;

namespace DeepVisionSharp.TorchSharp.Tensors;

public static class TorchSharpImageTensor
{
    public static Mat LoadMat(string imagePath, int expectedChannels = 3)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("图像文件不存在", imagePath);
        }

        var mode = expectedChannels == 1 ? ImreadModes.Grayscale : ImreadModes.Color;
        var mat = Cv2.ImRead(imagePath, mode);
        if (mat.Empty())
        {
            try
            {
                var bytes = File.ReadAllBytes(imagePath);
                var decoded = Cv2.ImDecode(bytes, mode);
                if (!decoded.Empty())
                {
                    mat.Dispose();
                    return decoded;
                }
                decoded.Dispose();
            }
            catch
            {
            }
        }

        if (mat.Empty())
        {
            mat.Dispose();
            throw new InvalidOperationException($"无法通过 OpenCV 读取图像: {imagePath}");
        }

        return mat;
    }

    public static torch.Tensor ToClassificationTensor(
        Mat sourceMat,
        int targetWidth,
        int targetHeight,
        torch.Device targetDevice,
        bool enableHorizontalFlip = false,
        bool enableVerticalFlip = false,
        float contrastScale = 1.0f,
        float brightnessDelta = 0.0f,
        PreprocessingVersion preprocessing = PreprocessingVersion.UnitScaleV1)
    {
        using var preparedMat = new Mat();
        if (sourceMat.Width != targetWidth || sourceMat.Height != targetHeight)
        {
            Cv2.Resize(sourceMat, preparedMat, new Size(targetWidth, targetHeight), 0, 0, InterpolationFlags.Linear);
        }
        else
        {
            sourceMat.CopyTo(preparedMat);
        }

        if (contrastScale != 1.0f || brightnessDelta != 0.0f)
        {
            preparedMat.ConvertTo(preparedMat, -1, contrastScale, brightnessDelta);
        }

        if (enableHorizontalFlip && enableVerticalFlip)
        {
            Cv2.Flip(preparedMat, preparedMat, FlipMode.XY);
        }
        else if (enableHorizontalFlip)
        {
            Cv2.Flip(preparedMat, preparedMat, FlipMode.Y);
        }
        else if (enableVerticalFlip)
        {
            Cv2.Flip(preparedMat, preparedMat, FlipMode.X);
        }

        return MatToNormalizedTensor(preparedMat, targetDevice, preprocessing);
    }

    private static readonly float[] ImageNetMean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] ImageNetStd = [0.229f, 0.224f, 0.225f];

    private static unsafe torch.Tensor MatToNormalizedTensor(Mat mat, torch.Device device, PreprocessingVersion preprocessing)
    {
        var channels = mat.Channels();
        var height = mat.Height;
        var width = mat.Width;

        using var rgbMat = new Mat();
        if (channels == 3)
        {
            Cv2.CvtColor(mat, rgbMat, ColorConversionCodes.BGR2RGB);
        }
        else
        {
            mat.CopyTo(rgbMat);
        }

        var totalPixels = height * width;
        var totalElements = totalPixels * channels;
        var floatBuffer = new float[totalElements];

        var matData = (byte*)rgbMat.Data.ToPointer();

        if (channels == 1)
        {
            for (var pixelIndex = 0; pixelIndex < totalPixels; pixelIndex++)
            {
                floatBuffer[pixelIndex] = matData[pixelIndex] / 255.0f;
            }
        }
        else if (channels == 3)
        {
            var channelSize = totalPixels;
            for (var rowIndex = 0; rowIndex < height; rowIndex++)
            {
                var rowPointer = matData + (rowIndex * rgbMat.Step());
                var rowOffset = rowIndex * width;
                for (var columnIndex = 0; columnIndex < width; columnIndex++)
                {
                    var pixelOffset = columnIndex * 3;
                    var bufferOffset = rowOffset + columnIndex;
                    floatBuffer[bufferOffset] = rowPointer[pixelOffset] / 255.0f;
                    floatBuffer[channelSize + bufferOffset] = rowPointer[pixelOffset + 1] / 255.0f;
                    floatBuffer[(channelSize * 2) + bufferOffset] = rowPointer[pixelOffset + 2] / 255.0f;
                }
            }
        }

        if (preprocessing == PreprocessingVersion.ImageNetV2 && channels == 3)
        {
            // ImageNet statistics: (value - mean) / std per channel.
            for (var channel = 0; channel < 3; channel++)
            {
                var mean = ImageNetMean[channel];
                var std = ImageNetStd[channel];
                var offset = channel * totalPixels;
                for (var pixel = 0; pixel < totalPixels; pixel++)
                {
                    floatBuffer[offset + pixel] = (floatBuffer[offset + pixel] - mean) / std;
                }
            }
        }

        // Build straight into the requested shape and device. `from_array(...).reshape(...)` would
        // allocate a second tensor that is never disposed, and a later `.to(device)` would leak the
        // CPU source as well; this path is called once per sample per epoch, so either leak grows
        // without bound over a training run.
        return torch.tensor(floatBuffer, [1, channels, height, width], device: device);
    }
}
