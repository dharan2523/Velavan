namespace VelavanDemo.Application.Attachments;

public sealed class AttachmentStorageOptions
{
    public const string SectionName = "Attachments:Storage";

    public string Provider { get; set; } = "Local";
    public string RootPath { get; set; } = string.Empty;
}
