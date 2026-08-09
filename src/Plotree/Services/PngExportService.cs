using SkiaSharp;
using System.Runtime.InteropServices.WindowsRuntime;
using Svg.Skia;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Plotree.Services;

/// <summary>Rasterizes the exported graph SVG directly into a verified PNG stream.</summary>
public static class PngExportService
{
    public static async Task ExportSvgAsync(
        string svg,
        IRandomAccessStream destination,
        PngExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(svg);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        using var renderer = new SKSvg();
        var picture = renderer.FromSvg(svg)
            ?? throw new InvalidDataException("The SVG export could not be parsed.");
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidDataException("The SVG export has invalid drawing bounds.");
        }

        var imageInfo = new SKImageInfo(
            checked((int)options.PixelWidth),
            checked((int)options.PixelHeight),
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo)
            ?? throw new InvalidOperationException("The PNG drawing surface could not be created.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(options.PixelWidth / bounds.Width, options.PixelHeight / bounds.Height);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("The PNG image could not be encoded.");
        using var staging = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(staging))
        {
            writer.WriteBytes(encoded.ToArray());
            await writer.StoreAsync().AsTask(cancellationToken);
            writer.DetachStream();
        }
        await VerifyAsync(staging, options, cancellationToken);

        staging.Seek(0);
        destination.Seek(0);
        destination.Size = 0;
        await RandomAccessStream.CopyAsync(staging, destination).AsTask(cancellationToken);
        await destination.FlushAsync().AsTask(cancellationToken);
        destination.Seek(0);
    }

    internal static void ValidateOptions(PngExportOptions options)
    {
        if (options.PixelWidth == 0 || options.PixelHeight == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "PNG dimensions must both be greater than zero.");
        }

        if (options.PixelWidth > PngExportOptions.MaxPixelDimension
            || options.PixelHeight > PngExportOptions.MaxPixelDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"PNG dimensions may not exceed {PngExportOptions.MaxPixelDimension} pixels.");
        }

        var pixelCount = checked((long)options.PixelWidth * options.PixelHeight);
        if (pixelCount > PngExportOptions.MaxPixelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"PNG output may not exceed {PngExportOptions.MaxPixelCount:N0} pixels.");
        }
    }

    private static async Task VerifyAsync(
        IRandomAccessStream stream,
        PngExportOptions options,
        CancellationToken cancellationToken)
    {
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        if (decoder.PixelWidth != options.PixelWidth || decoder.PixelHeight != options.PixelHeight)
        {
            throw new InvalidDataException(
                $"PNG verification decoded {decoder.PixelWidth} by {decoder.PixelHeight} pixels instead of {options.PixelWidth} by {options.PixelHeight}.");
        }

        var decoded = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage)
            .AsTask(cancellationToken);
        var pixels = decoded.DetachPixelData();
        var expectedLength = checked((long)options.PixelWidth * options.PixelHeight * 4);
        if (pixels.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"PNG verification decoded {pixels.Length:N0} bytes instead of {expectedLength:N0}.");
        }
    }
}
