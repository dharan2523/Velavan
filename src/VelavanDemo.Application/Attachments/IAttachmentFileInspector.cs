using VelavanDemo.Application.Common;
using VelavanDemo.Domain.Enums;

namespace VelavanDemo.Application.Attachments;

public interface IAttachmentFileInspector
{
    OperationResult<AttachmentFileInfo> Inspect(
        Stream content,
        AttachmentKind kind,
        string fileName,
        string? declaredContentType,
        long sizeBytes);
}
