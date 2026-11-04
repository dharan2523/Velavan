namespace VelavanDemo.Application.Common;

public sealed record OperationResult(
    bool Succeeded,
    OperationError Error = OperationError.None,
    string? Message = null)
{
    public static OperationResult Success()
    {
        return new OperationResult(true);
    }

    public static OperationResult Failure(OperationError error, string message)
    {
        return new OperationResult(false, error, message);
    }
}

public sealed record OperationResult<T>(
    bool Succeeded,
    T? Value,
    OperationError Error = OperationError.None,
    string? Message = null)
{
    public static OperationResult<T> Success(T value)
    {
        return new OperationResult<T>(true, value);
    }

    public static OperationResult<T> Failure(OperationError error, string message)
    {
        return new OperationResult<T>(false, default, error, message);
    }
}
