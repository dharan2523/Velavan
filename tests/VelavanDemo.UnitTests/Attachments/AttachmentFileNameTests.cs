using VelavanDemo.Application.Attachments;

namespace VelavanDemo.UnitTests.Attachments;

public sealed class AttachmentFileNameTests
{
    [Fact]
    public void Sanitize_RemovesPathSegmentsAndInvalidCharacters()
    {
        var result = AttachmentFileName.Sanitize("..\\..\\invoices\\bad:name?.pdf");

        Assert.Equal("bad_name_.pdf", result);
    }

    [Fact]
    public void Sanitize_ReturnsFallbackForBlankName()
    {
        Assert.Equal("attachment", AttachmentFileName.Sanitize("   "));
        Assert.Equal("attachment", AttachmentFileName.Sanitize(null));
    }

    [Fact]
    public void Sanitize_TruncatesLongNamesWhilePreservingExtension()
    {
        var fileName = new string('a', 300) + ".png";

        var result = AttachmentFileName.Sanitize(fileName);

        Assert.Equal(255, result.Length);
        Assert.EndsWith(".png", result, StringComparison.Ordinal);
    }
}
