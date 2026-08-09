using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Plotree.Services;

/// <summary>
/// Encodes and verifies raw premultiplied-BGRA frames independently of WinUI composition.
/// </summary>
public static class PngExportEncoding
{
    /// <summary>
    /// Encodes and verifies a PNG in an isolated stream, then replaces
    /// <paramref name="destination"/> only after Windows can decode the completed image at the
    /// requested dimensions.
    /// </summary>
    public static async Task EncodeAndVerifyAsync(
        IRandomAccessStream destination,
        uint pixelWidth,
        uint pixelHeight,
        byte[] pixels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(pixels);
        ValidateFrame(pixelWidth, pixelHeight, pixels.Length);

        using var staging = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, staging)
            .AsTask(cancellationToken);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            pixelWidth,
            pixelHeight,
            96,
            96,
            pixels);
        await encoder.FlushAsync().AsTask(cancellationToken);

        staging.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(staging).AsTask(cancellationToken);
        if (decoder.PixelWidth != pixelWidth || decoder.PixelHeight != pixelHeight)
        {
            throw new InvalidDataException(
                $"PNG verification decoded {decoder.PixelWidth} by {decoder.PixelHeight} pixels instead of {pixelWidth} by {pixelHeight}.");
        }

        // Force full pixel decoding, rather than merely accepting a PNG header.
        var decoded = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage)
            .AsTask(cancellationToken);
        if (decoded.DetachPixelData().Length != pixels.Length)
        {
            throw new InvalidDataException("PNG verification decoded an unexpected pixel buffer length.");
        }

        staging.Seek(0);
        destination.Seek(0);
        destination.Size = 0;
        await RandomAccessStream.CopyAsync(staging, destination).AsTask(cancellationToken);
        await destination.FlushAsync().AsTask(cancellationToken);
        destination.Seek(0);
    }

    /// <summary>Validates a raw BGRA frame before it is passed to the Windows encoder.</summary>
    public static void ValidateFrame(uint pixelWidth, uint pixelHeight, int pixelBufferLength)
    {
        var options = new PngExportOptions
        {
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
        };
        PngExportService.ValidateOptions(options);

        var expectedLength = checked((long)pixelWidth * pixelHeight * 4);
        if (pixelBufferLength != expectedLength)
        {
            throw new ArgumentException(
                $"The BGRA pixel buffer must contain exactly {expectedLength:N0} bytes, but contained {pixelBufferLength:N0} bytes.",
                nameof(pixelBufferLength));
        }
    }
}
