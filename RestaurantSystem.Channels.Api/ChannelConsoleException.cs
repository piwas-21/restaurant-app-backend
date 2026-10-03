namespace RestaurantSystem.Channels.Api;

public sealed class ChannelConsoleException(int status, string message, string? errorCode = null) : Exception(message)
{
    public int Status { get; } = status;
    public string? ErrorCode { get; } = errorCode;
}
