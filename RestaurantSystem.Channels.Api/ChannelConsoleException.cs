namespace RestaurantSystem.Channels.Api;

public sealed class ChannelConsoleException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
