using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Plotree.Services;

namespace Plotree.Tests;

[TestClass]
public sealed class PngExportEncodingTests
{
    [TestMethod]
    public async Task EncodeAndVerifyAsync_WritesADecodablePngWithExpectedPixels()
    {
        var pixels = new byte[]
        {
            0x00, 0x00, 0xFF, 0xFF, // red, BGRA
            0x00, 0xFF, 0x00, 0xFF, // green
            0xFF, 0x00, 0x00, 0xFF, // blue
            0xFF, 0xFF, 0xFF, 0xFF, // white
        };
        using var stream = new InMemoryRandomAccessStream();

        await PngExportEncoding.EncodeAndVerifyAsync(stream, 2, 2, pixels);

        stream.Seek(0);
        var reader = new DataReader(stream);
        await reader.LoadAsync(8);
        var signature = new byte[8];
        reader.ReadBytes(signature);
        CollectionAssert.AreEqual(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            signature);

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        Assert.AreEqual(2u, decoder.PixelWidth);
        Assert.AreEqual(2u, decoder.PixelHeight);
        var decoded = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        CollectionAssert.AreEqual(pixels, decoded.DetachPixelData());
    }

    [TestMethod]
    public void ValidateFrame_RejectsAnIncorrectPixelBufferLength()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => PngExportEncoding.ValidateFrame(2, 2, 15));
    }

    [TestMethod]
    public void ValidateFrame_RejectsUnsupportedDimensions()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => PngExportEncoding.ValidateFrame(0, 1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => PngExportEncoding.ValidateFrame(PngExportOptions.MaxPixelDimension + 1, 1, 0));
    }

    [TestMethod]
    public async Task EncodeAndVerifyAsync_DoesNotTruncateDestinationBeforeInputValidation()
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes([0x10, 0x20, 0x30]);
            await writer.StoreAsync();
            writer.DetachStream();
        }

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => PngExportEncoding.EncodeAndVerifyAsync(stream, 2, 2, new byte[1]));

        Assert.AreEqual(3ul, stream.Size);
        stream.Seek(0);
        var reader = new DataReader(stream);
        await reader.LoadAsync(3);
        var original = new byte[3];
        reader.ReadBytes(original);
        CollectionAssert.AreEqual(new byte[] { 0x10, 0x20, 0x30 }, original);
    }

    [TestMethod]
    public async Task EncodeAndVerifyAsync_WritesACompletePngToARealFileStream()
    {
        var path = Path.Combine(Path.GetTempPath(), $"plotree-{Guid.NewGuid():N}.png");
        var pixels = new byte[]
        {
            0x00, 0x00, 0x80, 0x80,
            0x40, 0x00, 0x00, 0x40,
        };

        try
        {
            await File.WriteAllBytesAsync(path, []);
            using (var destination = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.ReadWrite))
            {
                await PngExportEncoding.EncodeAndVerifyAsync(destination, 2, 1, pixels);
            }

            var bytes = await File.ReadAllBytesAsync(path);
            CollectionAssert.AreEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
                bytes[..8]);

            using var source = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(source);
            Assert.AreEqual(2u, decoder.PixelWidth);
            Assert.AreEqual(1u, decoder.PixelHeight);
            var decoded = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
            CollectionAssert.AreEqual(pixels, decoded.DetachPixelData());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task ExportSvgAsync_WritesADecodablePngWithVisiblePixels()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="120" height="60" viewBox="0 0 120 60">
              <rect x="0" y="0" width="120" height="60" fill="#FFFFFF" />
              <text x="10" y="35" fill="#111111" font-family="Segoe UI" font-size="20">Plotree</text>
            </svg>
            """;
        using var stream = new InMemoryRandomAccessStream();

        await PngExportService.ExportSvgAsync(
            svg,
            stream,
            new PngExportOptions
            {
                PixelWidth = 120,
                PixelHeight = 60,
            });

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        Assert.AreEqual(120u, decoder.PixelWidth);
        Assert.AreEqual(60u, decoder.PixelHeight);
        var decoded = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var pixels = decoded.DetachPixelData();
        Assert.IsTrue(
            Enumerable.Range(0, pixels.Length / 4).Any(index => pixels[index * 4 + 3] != 0),
            "The exported PNG should contain visible pixels.");
    }
}
