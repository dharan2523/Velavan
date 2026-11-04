using System.Buffers.Binary;
using System.Text;
using VelavanDemo.Application.Common;
using VelavanDemo.Domain.Enums;

namespace VelavanDemo.Application.Attachments;

public sealed class AttachmentFileInspector : IAttachmentFileInspector
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".pdf"] = "application/pdf"
        };

    private static readonly byte[] PngSignature =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
    };

    public OperationResult<AttachmentFileInfo> Inspect(
        Stream content,
        AttachmentKind kind,
        string fileName,
        string? declaredContentType,
        long sizeBytes)
    {
        var policy = AttachmentPolicies.For(kind);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (!policy.AllowedExtensions.Contains(extension))
        {
            return OperationResult<AttachmentFileInfo>.Failure(
                OperationError.UnsupportedFileType,
                "The file type is not allowed for this attachment.");
        }

        if (sizeBytes <= 0)
        {
            return OperationResult<AttachmentFileInfo>.Failure(
                OperationError.Validation,
                "The uploaded file is empty.");
        }

        if (sizeBytes > policy.MaxBytes)
        {
            return OperationResult<AttachmentFileInfo>.Failure(
                OperationError.FileTooLarge,
                $"The file exceeds the {FormatMegabytes(policy.MaxBytes)} MB limit.");
        }

        if (!ContentTypes.TryGetValue(extension, out var expectedContentType))
        {
            return OperationResult<AttachmentFileInfo>.Failure(
                OperationError.UnsupportedFileType,
                "The file type is not supported.");
        }

        var normalizedContentType = NormalizeContentType(declaredContentType);

        if (!string.Equals(normalizedContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<AttachmentFileInfo>.Failure(
                OperationError.UnsupportedFileType,
                "The declared file type does not match the file extension.");
        }

        if (!content.CanSeek)
        {
            return OperationResult<AttachmentFileInfo>.Failure(
                OperationError.Validation,
                "The uploaded file could not be inspected.");
        }

        var originalPosition = content.Position;

        try
        {
            content.Position = 0;

            int? width = null;
            int? height = null;

            if (expectedContentType == "application/pdf")
            {
                if (!HasPdfSignature(content))
                {
                    return InvalidContent();
                }
            }
            else
            {
                var dimensions = extension switch
                {
                    ".png" => ReadPngDimensions(content),
                    ".webp" => ReadWebpDimensions(content),
                    _ => ReadJpegDimensions(content)
                };

                if (dimensions is null)
                {
                    return InvalidContent();
                }

                width = dimensions.Value.Width;
                height = dimensions.Value.Height;

                if (width <= 0 ||
                    height <= 0 ||
                    width > AttachmentPolicies.MaxImageDimension ||
                    height > AttachmentPolicies.MaxImageDimension ||
                    (long)width.Value * height.Value > AttachmentPolicies.MaxImagePixels)
                {
                    return OperationResult<AttachmentFileInfo>.Failure(
                        OperationError.Validation,
                        "The image dimensions are not allowed.");
                }
            }

            return OperationResult<AttachmentFileInfo>.Success(
                new AttachmentFileInfo(extension, expectedContentType, width, height));
        }
        finally
        {
            content.Position = originalPosition;
        }
    }

    private static OperationResult<AttachmentFileInfo> InvalidContent()
    {
        return OperationResult<AttachmentFileInfo>.Failure(
            OperationError.UnsupportedFileType,
            "The file content does not match its extension or is corrupt.");
    }

    private static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }

        var separatorIndex = contentType.IndexOf(';');

        return (separatorIndex >= 0 ? contentType[..separatorIndex] : contentType)
            .Trim()
            .ToLowerInvariant();
    }

    private static string FormatMegabytes(long bytes)
    {
        return (bytes / (1024 * 1024)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool HasPdfSignature(Stream content)
    {
        Span<byte> header = stackalloc byte[5];

        if (content.Read(header) != header.Length)
        {
            return false;
        }

        return header.SequenceEqual("%PDF-"u8);
    }

    private static (int Width, int Height)? ReadPngDimensions(Stream content)
    {
        Span<byte> header = stackalloc byte[24];

        if (content.Read(header) != header.Length ||
            !header[..8].SequenceEqual(PngSignature) ||
            !header[12..16].SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);

        return (width, height);
    }

    private static (int Width, int Height)? ReadWebpDimensions(Stream content)
    {
        Span<byte> header = stackalloc byte[30];

        var bytesRead = ReadUpTo(content, header);
        var data = header[..bytesRead];

        if (bytesRead < 30 ||
            !data[..4].SequenceEqual("RIFF"u8) ||
            !data[8..12].SequenceEqual("WEBP"u8))
        {
            return null;
        }

        var chunkType = Encoding.ASCII.GetString(data[12..16]);

        if (chunkType == "VP8X")
        {
            var width = (data[24] | (data[25] << 8) | (data[26] << 16)) + 1;
            var height = (data[27] | (data[28] << 8) | (data[29] << 16)) + 1;

            return (width, height);
        }

        if (chunkType == "VP8 ")
        {
            if (data[23] != 0x9D || data[24] != 0x01 || data[25] != 0x2A)
            {
                return null;
            }

            var width = (data[26] | (data[27] << 8)) & 0x3FFF;
            var height = (data[28] | (data[29] << 8)) & 0x3FFF;

            return width == 0 || height == 0 ? null : (width, height);
        }

        if (chunkType == "VP8L")
        {
            if (data[20] != 0x2F)
            {
                return null;
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..25]);
            var width = (int)(bits & 0x3FFF) + 1;
            var height = (int)((bits >> 14) & 0x3FFF) + 1;

            return (width, height);
        }

        return null;
    }

    private static (int Width, int Height)? ReadJpegDimensions(Stream content)
    {
        if (content.ReadByte() != 0xFF || content.ReadByte() != 0xD8)
        {
            return null;
        }

        var scanStart = content.Position;

        while (content.Position - scanStart <= 1_048_576)
        {
            var prefix = content.ReadByte();

            if (prefix < 0)
            {
                return null;
            }

            if (prefix != 0xFF)
            {
                continue;
            }

            int marker;

            do
            {
                marker = content.ReadByte();
            }
            while (marker == 0xFF);

            if (marker < 0 || marker == 0xD9 || marker == 0xDA)
            {
                return null;
            }

            if (marker == 0x00 ||
                marker == 0x01 ||
                marker == 0xD8 ||
                (marker >= 0xD0 && marker <= 0xD7))
            {
                continue;
            }

            var lengthHigh = content.ReadByte();
            var lengthLow = content.ReadByte();

            if (lengthHigh < 0 || lengthLow < 0)
            {
                return null;
            }

            var segmentLength = (lengthHigh << 8) | lengthLow;

            if (segmentLength < 2)
            {
                return null;
            }

            if ((marker >= 0xC0 && marker <= 0xC3) ||
                (marker >= 0xC5 && marker <= 0xC7) ||
                (marker >= 0xC9 && marker <= 0xCB) ||
                (marker >= 0xCD && marker <= 0xCF))
            {
                if (content.ReadByte() < 0)
                {
                    return null;
                }

                Span<byte> dimensions = stackalloc byte[4];

                if (content.Read(dimensions) != dimensions.Length)
                {
                    return null;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(dimensions[..2]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(dimensions[2..]);

                return width == 0 || height == 0 ? null : (width, height);
            }

            content.Seek(segmentLength - 2, SeekOrigin.Current);
        }

        return null;
    }

    private static int ReadUpTo(Stream content, Span<byte> buffer)
    {
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var read = content.Read(buffer[totalRead..]);

            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }
}
