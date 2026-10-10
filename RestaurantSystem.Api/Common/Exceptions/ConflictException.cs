namespace RestaurantSystem.Api.Common.Exceptions;

/// <summary>Raised when a concurrent write means the caller must reload before retrying.</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }

    public ConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ConflictException(string message, string errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }

    public ConflictException(string message, string errorCode, int currentRevision) : base(message)
    {
        ErrorCode = errorCode;
        CurrentRevision = currentRevision;
    }

    public ConflictException(string message, Exception innerException, string errorCode, int currentRevision)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        CurrentRevision = currentRevision;
    }

    public string? ErrorCode { get; }
    public int? CurrentRevision { get; }
}
