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
}
