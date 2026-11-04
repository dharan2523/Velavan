namespace VelavanDemo.Application.Attachments;

public sealed class AttachmentFileTooLargeException : Exception
{
    public AttachmentFileTooLargeException()
        : base("The uploaded file exceeds the allowed size.")
    {
    }
}
