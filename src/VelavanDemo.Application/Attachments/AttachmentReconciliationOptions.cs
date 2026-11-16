namespace VelavanDemo.Application.Attachments;

public sealed class AttachmentReconciliationOptions
{
    public const string SectionName = "Attachments:Reconciliation";

    public bool Enabled { get; set; }
    public bool RunOnStartup { get; set; }
    public int IntervalMinutes { get; set; } = 1_440;
    public int OrphanGracePeriodMinutes { get; set; } = 30;
}
