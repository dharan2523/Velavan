namespace VelavanDemo.Application.Attachments;

public sealed class AttachmentScannerOptions
{
    public const string SectionName = "Attachments:Scanner";
    public const string NoOpProvider = "None";
    public const string HttpProvider = "Http";

    public string Provider { get; set; } = NoOpProvider;
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}
