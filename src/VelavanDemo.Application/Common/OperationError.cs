namespace VelavanDemo.Application.Common;

public enum OperationError
{
    None = 0,
    NotFound = 1,
    Validation = 2,
    DuplicateEmail = 3,
    InvalidRole = 4,
    Forbidden = 5,
    CurrentPasswordIncorrect = 6,
    PasswordReuse = 7,
    LastSuperAdmin = 8,
    SelfModification = 9,
    DuplicateUsername = 10,
    FileTooLarge = 11,
    UnsupportedFileType = 12,
    AttachmentLimitReached = 13,
    AttachmentConflict = 14,
    StorageFailure = 15,
    MalwareDetected = 16,
    ScanUnavailable = 17,
    StorageQuotaExceeded = 18
}
