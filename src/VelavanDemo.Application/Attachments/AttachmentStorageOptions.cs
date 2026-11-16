namespace VelavanDemo.Application.Attachments;

public sealed class AttachmentStorageOptions
{
    public const string SectionName = "Attachments:Storage";

    public const string DefaultRootPath = @"C:\ProgramData\VelavanDemo\attachments";

    public string Provider { get; set; } = "Local";
    public string RootPath { get; set; } = DefaultRootPath;
    public long MaxTotalBytes { get; set; }
    public string ServiceAccount { get; set; } = string.Empty;
}
