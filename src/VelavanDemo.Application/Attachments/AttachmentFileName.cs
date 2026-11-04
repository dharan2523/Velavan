using System.Text;

namespace VelavanDemo.Application.Attachments;

public static class AttachmentFileName
{
    private const int MaxFileNameLength = 255;

    public static string Sanitize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "attachment";
        }

        var rawName = fileName.Trim();
        var lastSlash = rawName.LastIndexOf('/');
        var lastBackslash = rawName.LastIndexOf('\\');
        var slashIndex = lastSlash > lastBackslash ? lastSlash : lastBackslash;

        if (slashIndex >= 0)
        {
            rawName = rawName.Substring(slashIndex + 1);
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(rawName.Length);

        foreach (var character in rawName)
        {
            var isInvalid = char.IsControl(character) ||
                Array.IndexOf(invalidCharacters, character) >= 0;

            builder.Append(isInvalid ? '_' : character);
        }

        var sanitized = builder.ToString().Trim().Trim('.', ' ');

        if (sanitized.Length == 0)
        {
            return "attachment";
        }

        if (sanitized.Length <= MaxFileNameLength)
        {
            return sanitized;
        }

        var extension = Path.GetExtension(sanitized);
        var baseName = Path.GetFileNameWithoutExtension(sanitized);

        if (extension.Length >= MaxFileNameLength)
        {
            return sanitized.Substring(0, MaxFileNameLength);
        }

        var maxBaseNameLength = MaxFileNameLength - extension.Length;
        var truncatedBaseName = baseName.Substring(
            0,
            Math.Min(baseName.Length, maxBaseNameLength));

        return truncatedBaseName + extension;
    }
}
