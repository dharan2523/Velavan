using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VelavanDemo.Application.Attachments;
using VelavanDemo.Infrastructure.Attachments;

namespace VelavanDemo.UnitTests.Attachments;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        "VelavanDemo.LocalFileStorageTests",
        Guid.NewGuid().ToString("N"));

    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _storage = new LocalFileStorage(
            Options.Create(new AttachmentStorageOptions { RootPath = _rootPath }),
            NullLogger<LocalFileStorage>.Instance);
    }

    [Fact]
    public async Task SaveAsync_StreamsContentGeneratesSafeKeyAndComputesSha256()
    {
        var content = Encoding.UTF8.GetBytes("attachment-content");

        await using var source = new MemoryStream(content);

        var stored = await _storage.SaveAsync(
            source,
            ".PDF",
            AttachmentPolicies.DocumentMaxBytes);

        Assert.Equal(LocalFileStorage.ProviderName, stored.Provider);
        Assert.Equal(content.Length, stored.SizeBytes);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            stored.Sha256);
        Assert.EndsWith(".pdf", stored.StorageKey, StringComparison.Ordinal);
        Assert.DoesNotContain("attachment-content", stored.StorageKey);
        Assert.Matches(
            @"^\d{4}/\d{2}/[0-9a-f]{32}\.pdf$",
            stored.StorageKey);

        var physicalPath = Path.Combine(
            _rootPath,
            stored.StorageKey.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(physicalPath));
        Assert.Equal(content, await File.ReadAllBytesAsync(physicalPath));
    }

    [Fact]
    public async Task SaveAsync_RejectsContentAboveLimitAndRemovesTemporaryFile()
    {
        await using var source = new MemoryStream(new byte[32]);

        await Assert.ThrowsAsync<AttachmentFileTooLargeException>(() =>
            _storage.SaveAsync(source, ".pdf", maxBytes: 16));

        Assert.Empty(Directory.GetFiles(_rootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsStoredContent()
    {
        var content = Encoding.UTF8.GetBytes("read-me");
        await using var source = new MemoryStream(content);

        var stored = await _storage.SaveAsync(
            source,
            ".pdf",
            AttachmentPolicies.DocumentMaxBytes);

        await using var result = await _storage.OpenReadAsync(stored.StorageKey);

        using var copy = new MemoryStream();
        await result.CopyToAsync(copy);

        Assert.Equal(content, copy.ToArray());
    }

    [Fact]
    public async Task DeleteAsync_RemovesPhysicalFileAndIgnoresMissingFile()
    {
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("delete-me"));

        var stored = await _storage.SaveAsync(
            source,
            ".png",
            AttachmentPolicies.ProductImageMaxBytes);

        var physicalPath = Path.Combine(
            _rootPath,
            stored.StorageKey.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(physicalPath));

        await _storage.DeleteAsync(stored.StorageKey);
        Assert.False(File.Exists(physicalPath));

        await _storage.DeleteAsync(stored.StorageKey);
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("..\\outside.pdf")]
    [InlineData("/absolute.pdf")]
    public async Task OpenReadAsync_RejectsStorageKeysOutsideTheRoot(string storageKey)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _storage.OpenReadAsync(storageKey));
    }

    [Fact]
    public void Constructor_RejectsStorageRootInsideContentRoot()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new LocalFileStorage(
                Options.Create(new AttachmentStorageOptions { RootPath = _rootPath }),
                NullLogger<LocalFileStorage>.Instance,
                new TestHostEnvironment { ContentRootPath = _rootPath }));

        Assert.Contains("content root", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_RejectsFilesystemRoot()
    {
        var driveRoot = Path.GetPathRoot(_rootPath);

        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            return;
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new LocalFileStorage(
                Options.Create(new AttachmentStorageOptions { RootPath = driveRoot }),
                NullLogger<LocalFileStorage>.Instance));

        Assert.Contains("filesystem root", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Constructor_HardensWindowsStorageAcl()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directoryInfo = new DirectoryInfo(_rootPath);
        var security = directoryInfo.GetAccessControl(AccessControlSections.Access);
        var accessRules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();

        Assert.True(security.AreAccessRulesProtected);
        Assert.NotEmpty(accessRules);
        Assert.Contains(
            accessRules,
            rule => rule.AccessControlType == AccessControlType.Allow);
    }

    [Fact]
    public void ValidateStorageAccess_WhenProbeCanWriteReadAndDelete_DoesNotThrow()
    {
        Assert.True(Directory.Exists(_rootPath));
        var storage = new LocalFileStorage(
            Options.Create(new AttachmentStorageOptions { RootPath = _rootPath }),
            NullLogger<LocalFileStorage>.Instance);

        storage.ValidateStorageAccess();
    }

    [Fact]
    public async Task OpenReadAsync_RejectsWindowsAlternateDataStreamKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _storage.OpenReadAsync("2026/01/image.png:evil"));
    }

    [Fact]
    public async Task Inventory_IgnoresTemporaryUploadFiles()
    {
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("inventory"));
        var stored = await _storage.SaveAsync(
            source,
            ".pdf",
            AttachmentPolicies.DocumentMaxBytes);

        var directory = Path.GetDirectoryName(Path.Combine(
            _rootPath,
            stored.StorageKey.Replace('/', Path.DirectorySeparatorChar)))!;
        var temporaryPath = Path.Combine(directory, ".upload-test.tmp");
        await File.WriteAllTextAsync(temporaryPath, "partial");

        var keys = await _storage.ListStorageKeysAsync();
        var totalBytes = await _storage.GetTotalSizeBytesAsync();

        Assert.Contains(stored.StorageKey, keys);
        Assert.DoesNotContain(".upload-test.tmp", keys);
        Assert.Equal(new FileInfo(Path.Combine(
            _rootPath,
            stored.StorageKey.Replace('/', Path.DirectorySeparatorChar))).Length, totalBytes);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData(".p df")]
    [InlineData(".toolongextension")]
    public async Task SaveAsync_RejectsUnsafeExtensions(string extension)
    {
        await using var source = new MemoryStream(new byte[8]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _storage.SaveAsync(source, extension, maxBytes: 16));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "VelavanDemo.UnitTests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
