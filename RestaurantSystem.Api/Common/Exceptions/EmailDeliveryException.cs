namespace RestaurantSystem.Api.Common.Exceptions;

/// <summary>
/// Raised when an email could not be delivered by the configured transport
/// (e.g. the Resend API returned a non-success response). Maps to 502 Bad Gateway
/// so an upstream provider failure isn't misreported as an internal bug.
/// </summary>
public class EmailDeliveryException : Exception
{
    public EmailDeliveryException() : base("Email delivery failed")
    {
    }

    public EmailDeliveryException(string message) : base(message)
    {
    }

    public EmailDeliveryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
