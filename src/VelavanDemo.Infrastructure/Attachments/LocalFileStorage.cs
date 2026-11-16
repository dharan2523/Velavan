using System.Buffers;
using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VelavanDemo.Application.Attachments;

namespace VelavanDemo.Infrastructure.Attachments;

public sealed class LocalFileStorage : IFileStorage, IFileStorageInventory
{
    public const string ProviderName = "Local";

    private const int BufferSize = 81_920;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private string _rootPath;
    private readonly ILogger<LocalFileStorage> _logger;
    private readonly string? _serviceAccount;

    public LocalFileStorage(
        IOptions<AttachmentStorageOptions> options,
        ILogger<LocalFileStorage> logger,
        IHostEnvironment? hostEnvironment = null)
    {
        var storageOptions = options.Value;
        var configuredRootPath = storageOptions.RootPath;
        _serviceAccount = storageOptions.ServiceAccount;

        if (!string.Equals(
                storageOptions.Provider,
                ProviderName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Attachment storage provider '{storageOptions.Provider}' is not supported.");
        }

        if (string.IsNullOrWhiteSpace(configuredRootPath))
        {
            throw new InvalidOperationException(
                $"Attachment storage root path '{AttachmentStorageOptions.SectionName}:RootPath' is not configured.");
        }

        _rootPath = Path.GetFullPath(
            Path.IsPathRooted(configuredRootPath)
                ? configuredRootPath
                : Path.Combine(AppContext.BaseDirectory, configuredRootPath));

        ValidateRootPath(hostEnvironment);
        Directory.CreateDirectory(_rootPath);

        if (OperatingSystem.IsWindows())
        {
            ApplyWindowsAccessControl(_rootPath, _serviceAccount);
        }
        else
        {
            ApplyUnixAccessControl(_rootPath);
        }

        ValidateStorageAccess();
        _logger = logger;
    }

    public void ValidateStorageAccess()
    {
        if (!Directory.Exists(_rootPath))
        {
            throw new DirectoryNotFoundException(
                $"Attachment storage root path '{_rootPath}' does not exist.");
        }

        var probePath = Path.Combine(_rootPath, $".upload-probe-{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                stream.WriteByte(0);
            }

            using (var readStream = new FileStream(
                probePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                readStream.ReadByte();
            }
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Startup attachment storage probe temporary file {TemporaryPath} could not be removed.",
                    probePath);
            }
        }
    }

    public async Task<StoredFile> SaveAsync(
        Stream content,
        string fileExtension,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        var normalizedExtension = NormalizeExtension(fileExtension);
        var now = DateTime.UtcNow;
        var storageKey = string.Join(
            '/',
            now.ToString("yyyy", CultureInfo.InvariantCulture),
            now.ToString("MM", CultureInfo.InvariantCulture),
            $"{Guid.NewGuid():N}{normalizedExtension}");

        var finalPath = ResolvePath(storageKey);
        var directory = Path.GetDirectoryName(finalPath)
            ?? throw new InvalidOperationException("Attachment storage path is invalid.");
        var temporaryPath = Path.Combine(directory, $".upload-{Guid.NewGuid():N}.tmp");

        Directory.CreateDirectory(directory);
        EnsureNoReparsePoints(directory);

        if (OperatingSystem.IsWindows())
        {
            ApplyWindowsAccessControl(directory, _serviceAccount);
        }
        else
        {
            ApplyUnixAccessControl(directory);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var completed = false;

        try
        {
            long totalBytes = 0;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var bytesRead = 0;

                while ((bytesRead = await content.ReadAsync(
                           buffer.AsMemory(0, BufferSize),
                           cancellationToken)) > 0)
                {
                    totalBytes += bytesRead;

                    if (totalBytes > maxBytes)
                    {
                        throw new AttachmentFileTooLargeException();
                    }

                    hash.AppendData(buffer, 0, bytesRead);
                    await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }

                await target.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, finalPath);
            completed = true;

            var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

            _logger.LogInformation(
                "Stored attachment {StorageProvider} file with key {StorageKey} ({SizeBytes} bytes).",
                ProviderName,
                storageKey,
                totalBytes);

            return new StoredFile(ProviderName, storageKey, totalBytes, sha256);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);

            if (!completed && File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Temporary attachment file {TemporaryPath} could not be removed.",
                        temporaryPath);
                }
            }
        }
    }

    public Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolvePath(storageKey);

        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Task.FromResult(stream);
    }

    public Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolvePath(storageKey);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListStorageKeysAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootPath))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var keys = new List<string>();
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var path in Directory.EnumerateFiles(_rootPath, "*", enumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Path.GetFileName(path).StartsWith(".upload-", StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(_rootPath, path);
            keys.Add(relativePath.Replace(Path.DirectorySeparatorChar, '/'));
        }

        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    public Task<long> GetTotalSizeBytesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootPath))
        {
            return Task.FromResult(0L);
        }

        long totalBytes = 0;
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var path in Directory.EnumerateFiles(_rootPath, "*", enumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Path.GetFileName(path).StartsWith(".upload-", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                totalBytes += new FileInfo(path).Length;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return Task.FromResult(totalBytes);
    }

    public Task<DateTimeOffset?> GetLastWriteTimeUtcAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(storageKey);

        if (!File.Exists(path))
        {
            return Task.FromResult<DateTimeOffset?>(null);
        }

        return Task.FromResult<DateTimeOffset?>(new DateTimeOffset(
            File.GetLastWriteTimeUtc(path),
            TimeSpan.Zero));
    }

    [SupportedOSPlatform("windows")]
    private void ApplyWindowsAccessControl(string path, string? serviceAccount)
    {
        var serviceIdentifier = ResolveSecurityIdentifier(serviceAccount);
        var currentIdentifier = ResolveCurrentSecurityIdentifier();
        var systemIdentifier = ResolveSecurityIdentifier("NT AUTHORITY\\SYSTEM");
        var administratorsIdentifier = ResolveSecurityIdentifier("BUILTIN\\Administrators");

        var allowedIdentifiers = new List<SecurityIdentifier>
        {
            serviceIdentifier,
            currentIdentifier,
            systemIdentifier,
            administratorsIdentifier
        };

        var directoryInfo = new DirectoryInfo(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);

        foreach (var identity in allowedIdentifiers)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        directoryInfo.SetAccessControl(security);
        ValidateWindowsAccessControl(directoryInfo);
    }

    [SupportedOSPlatform("windows")]
    private void ValidateWindowsAccessControl(DirectoryInfo directoryInfo)
    {
        var security = directoryInfo.GetAccessControl(AccessControlSections.Access);

        if (!security.AreAccessRulesProtected)
        {
            throw new InvalidOperationException(
                "Attachment storage ACL must protect access rules from inheritance.");
        }

        var accessRules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
        var fileSystemRules = new List<FileSystemAccessRule>();

        foreach (var accessRule in accessRules)
        {
            if (accessRule is not FileSystemAccessRule fileSystemAccessRule)
            {
                continue;
            }

            if (fileSystemAccessRule.AccessControlType == AccessControlType.Deny)
            {
                throw new InvalidOperationException(
                    "Attachment storage ACL must not contain deny rules.");
            }

            if (IsBroadStorageIdentity(fileSystemAccessRule.IdentityReference))
            {
                throw new InvalidOperationException(
                    "Attachment storage ACL must not grant access to broad built-in identities.");
            }

            fileSystemRules.Add(fileSystemAccessRule);
        }

        var currentIdentity = ResolveCurrentSecurityIdentifier();
        var currentRule = fileSystemRules.FirstOrDefault(rule =>
            rule.IdentityReference is SecurityIdentifier identifier &&
            string.Equals(identifier.Value, currentIdentity.Value, StringComparison.OrdinalIgnoreCase) &&
            rule.AccessControlType == AccessControlType.Allow);

        if (currentRule is null)
        {
            throw new InvalidOperationException(
                "Attachment storage ACL must grant the API service identity full control.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier ResolveSecurityIdentifier(string? accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return ResolveCurrentSecurityIdentifier();
        }

        var identity = new NTAccount(accountName);
        var securityIdentifier = identity.Translate(typeof(SecurityIdentifier)) as SecurityIdentifier;

        if (securityIdentifier is null)
        {
            throw new InvalidOperationException(
                $"Attachment storage API service identity '{accountName}' could not be resolved.");
        }

        return securityIdentifier;
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier ResolveCurrentSecurityIdentifier()
    {
        var user = WindowsIdentity.GetCurrent().User;

        if (user is null)
        {
            throw new InvalidOperationException(
                "Attachment storage API service identity could not be resolved.");
        }

        return user;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsBroadStorageIdentity(IdentityReference identity)
    {
        var identifier = identity as SecurityIdentifier;

        if (identifier is null)
        {
            return false;
        }

        var value = identifier.Value;

        return value is
            "S-1-1-0" or
            "S-1-2-0" or
            "S-1-5-32-545" or
            "S-1-5-32-128" or
            "S-1-5-32-554";
    }

#pragma warning disable CA1416
    private void ApplyUnixAccessControl(string path)
    {
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        ValidateUnixAccessControl(path);
    }

#pragma warning disable CA1416
    private void ValidateUnixAccessControl(string path)
    {
        var permissions = File.GetUnixFileMode(path);
        var permissiveBits = permissions & (
            UnixFileMode.GroupRead |
            UnixFileMode.GroupWrite |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherWrite |
            UnixFileMode.OtherExecute);

        if (permissiveBits != UnixFileMode.None)
        {
            throw new InvalidOperationException(
                "Attachment storage permissions must restrict access to the owning service user.");
        }
    }

#pragma warning restore CA1416

    private void ValidateRootPath(IHostEnvironment? hostEnvironment)
    {
        if (IsDriveRoot(_rootPath))
        {
            throw new InvalidOperationException(
                "Attachment storage root path must not be a filesystem root.");
        }

        if (hostEnvironment is not null)
        {
            RejectRootInside(_rootPath, hostEnvironment.ContentRootPath, "content root");
        }

        if (!Directory.Exists(_rootPath))
        {
            return;
        }

        var rootInfo = new DirectoryInfo(_rootPath);

        if (!rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return;
        }

        var resolvedRoot = rootInfo.ResolveLinkTarget(returnFinalTarget: true);

        if (resolvedRoot is null)
        {
            throw new InvalidOperationException(
                "Attachment storage root path could not be resolved safely.");
        }

        var resolvedPath = Path.GetFullPath(resolvedRoot.FullName);

        if (hostEnvironment is not null)
        {
            RejectRootInside(resolvedPath, hostEnvironment.ContentRootPath, "content root");
        }

        _rootPath = resolvedPath;
    }

    private static void RejectRootInside(string rootPath, string? protectedPath, string label)
    {
        if (string.IsNullOrWhiteSpace(protectedPath))
        {
            return;
        }

        var normalizedProtectedPath = Path.GetFullPath(protectedPath);

        if (IsSamePath(rootPath, normalizedProtectedPath) ||
            IsChildPath(rootPath, normalizedProtectedPath))
        {
            throw new InvalidOperationException(
                $"Attachment storage root path must be outside the application {label}.");
        }
    }

    private string ResolvePath(string storageKey)
    {
        if (!IsSafeStorageKey(storageKey))
        {
            throw new InvalidOperationException("Attachment storage key is invalid.");
        }

        var relativePath = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        var rootPrefix = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, PathComparison))
        {
            throw new InvalidOperationException("Attachment storage key is invalid.");
        }

        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    private static bool IsSafeStorageKey(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) ||
            Path.IsPathRooted(storageKey) ||
            storageKey.Contains('\0') ||
            (OperatingSystem.IsWindows() && storageKey.Contains(':')))
        {
            return false;
        }

        var segments = storageKey.Split(
            new[] { '/', '\\' },
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Length > 0 &&
               segments.All(segment => segment is not "." and not "..");
    }

    private void EnsureNoReparsePoints(string fullPath)
    {
        var relativePath = Path.GetRelativePath(_rootPath, fullPath);

        if (relativePath == ".")
        {
            return;
        }

        var currentPath = _rootPath;

        foreach (var segment in relativePath.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);

            if (!File.Exists(currentPath) && !Directory.Exists(currentPath))
            {
                continue;
            }

            if (File.GetAttributes(currentPath).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    "Attachment storage paths must not contain symbolic links or junctions.");
            }
        }
    }

    private static bool IsDriveRoot(string path)
    {
        var pathRoot = Path.GetPathRoot(path);

        return !string.IsNullOrEmpty(pathRoot) &&
               IsSamePath(path, pathRoot);
    }

    private static bool IsSamePath(string first, string second)
    {
        return string.Equals(
            Path.GetFullPath(first).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar),
            PathComparison);
    }

    private static bool IsChildPath(string candidate, string parent)
    {
        var normalizedParent = Path.GetFullPath(parent).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return Path.GetFullPath(candidate).StartsWith(normalizedParent, PathComparison);
    }

    private static string NormalizeExtension(string fileExtension)
    {
        var normalized = (fileExtension ?? string.Empty).Trim().ToLowerInvariant();

        if (normalized.Length < 2 ||
            normalized.Length > 11 ||
            normalized[0] != '.' ||
            normalized[1..].Any(character => !char.IsLetterOrDigit(character)))
        {
            throw new InvalidOperationException("Attachment file extension is invalid.");
        }

        return normalized;
    }
}
