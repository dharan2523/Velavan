namespace VelavanDemo.Application.Attachments;

public sealed class ReorderProductImagesRequest
{
    public List<int> AttachmentIds { get; init; } = new();
}
