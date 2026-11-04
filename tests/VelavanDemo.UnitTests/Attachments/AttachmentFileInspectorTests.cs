using System.Buffers.Binary;
using System.Text;
using VelavanDemo.Application.Attachments;
using VelavanDemo.Application.Common;
using VelavanDemo.Domain.Enums;

namespace VelavanDemo.UnitTests.Attachments;

public sealed class AttachmentFileInspectorTests
{
    private readonly AttachmentFileInspector _inspector = new();

    [Fact]
    public void Inspect_AcceptsPngAndReadsDimensions()
    {
        var bytes = CreatePngHeader(width: 640, height: 480);

        using var stream = new MemoryStream(bytes);

        var result = _inspector.Inspect(
            stream,
            AttachmentKind.ProductImage,
            "product.png",
            "image/png",
            bytes.Length);

        Assert.True(result.Succeeded);
        Assert.Equal("image/png", result.Value!.ContentType);
        Assert.Equal(".png", result.Value.FileExtension);
        Assert.Equal(640, result.Value.Width);
        Assert.Equal(480, result.Value.Height);
    }

    [Fact]
    public void Inspect_AcceptsJpegAndReadsDimensions()
    {
        var bytes = CreateJpegHeader(width: 320, height: 200);

        using var stream = new MemoryStream(bytes);

        var result = _inspector.Inspect(
            stream,
            AttachmentKind.ProductImage,
            "photo.jpg",
            "image/jpeg",
            bytes.Length);

        Assert.True(result.Succeeded);
        Assert.Equal(320, result.Value!.Width);
        Assert.Equal(200, result.Value.Height);
    }

    [Fact]
    public void Inspect_AcceptsWebpAndReadsDimensions()
    {
        var bytes = CreateLosslessWebpHeader(width: 12, height: 34);

        using var stream = new MemoryStream(bytes);

        var result = _inspector.Inspect(
            stream,
            AttachmentKind.ProductImage,
            "image.webp",
            "image/webp",
            bytes.Length);

        Assert.True(result.Succeeded);
        Assert.Equal(12, result.Value!.Width);
        Assert.Equal(34, result.Value.Height);
    }

    [Fact]
    public void Inspect_AcceptsPdfDocument()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n");

        using var stream = new MemoryStream(bytes);

        var result = _inspector.Inspect(
            stream,
            AttachmentKind.CustomerDocument,
            "invoice.pdf",
            "application/pdf",
            bytes.Length);

        Assert.True(result.Succeeded);
        Assert.Equal("application/pdf", result.Value!.ContentType);
        Assert.Null(result.Value.Width);
        Assert.Null(result.Value.Height);
    }

    [Fact]
    public void Inspect_RejectsExtensionNotAllowedForKind()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n");

        using var stream = new MemoryStream(bytes);

        var result = _inspector.Inspect(
            stream,
            AttachmentKind.ProductImage,
            "invoice.pdf",
            "application/pdf",
            bytes.Length);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationError.UnsupportedFileType, result.Error);
    }

    [Fact]
    public void Inspect_RejectsMismatchedDeclaredContentType()
    {
        var bytes = CreatePngHeader(width: 10, height: 10);

        using var stream = new MemoryStream(bytes);

        var result = _inspector.Inspect(
            stream,
            AttachmentKind.ProductImage,
            "image.png",
            "image/jpeg",
            bytes.Length);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationError.UnsupportedFileType, result.Error);
    }

    [Fact]
    public void Inspect_RejectsContentThatDoesNotMatchExtension()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n");

        using var stream = new MemoryStream(bytes);

        var result = _inspector.Inspect(
            stream,
            AttachmentKind.ProductImage,
            "image.png",
            "image/png",
            bytes.Length);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationError.UnsupportedFileType, result.Error);
    }

    [Fact]
    public void Inspect_RejectsOversizedFile()
    {
        var bytes = CreatePngHeader(width: 10, height: 10);

        using var stream = new MemoryStream(bytes);

        var result = _inspector.Inspect(
            stream,
            AttachmentKind.ProductImage,
            "image.png",
            "image/png",
            AttachmentPolicies.ProductImageMaxBytes + 1);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationError.FileTooLarge, result.Error);
    }

    [Fact]
    public void Inspect_RejectsExcessiveImageDimensions()
    {
        var bytes = CreatePngHeader(width: 20_000, height: 20_000);

        using var stream = new MemoryStream(bytes);

        var result = _inspector.Inspect(
            stream,
            AttachmentKind.ProductImage,
            "image.png",
            "image/png",
            bytes.Length);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationError.Validation, result.Error);
    }

    private static byte[] CreatePngHeader(int width, int height)
    {
        var bytes = new byte[24];
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        signature.CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);

        return bytes;
    }

    private static byte[] CreateJpegHeader(int width, int height)
    {
        var bytes = new byte[30];

        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        bytes[3] = 0xC0;
        bytes[4] = 0x00;
        bytes[5] = 0x11;
        bytes[6] = 0x08;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(7, 2), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(9, 2), (ushort)width);

        return bytes;
    }

    private static byte[] CreateLosslessWebpHeader(int width, int height)
    {
        var bytes = new byte[30];

        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), 22);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(bytes, 8);
        Encoding.ASCII.GetBytes("VP8L").CopyTo(bytes, 12);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), 4);
        bytes[20] = 0x2F;

        var bits = (uint)((width - 1) | ((height - 1) << 14));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(21, 4), bits);

        return bytes;
    }
}
