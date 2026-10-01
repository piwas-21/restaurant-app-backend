namespace RestaurantSystem.Channels.Api;

public interface IChannelCommand<TResult>;
public interface IChannelCommandHandler<in TCommand, TResult> where TCommand : IChannelCommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}

// Custom CQRS dispatch, matching the tenant API's convention without importing its runtime.
public sealed class ChannelMediator(IServiceProvider services)
{
    public Task<TResult> SendCommand<TCommand, TResult>(TCommand command, CancellationToken cancellationToken)
        where TCommand : IChannelCommand<TResult>
        => services.GetRequiredService<IChannelCommandHandler<TCommand, TResult>>().Handle(command, cancellationToken);
}
