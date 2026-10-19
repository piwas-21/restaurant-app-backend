namespace RestaurantSystem.Api.Features.Basket.Interfaces;

public interface IBasketWriteCoordinator
{
    Task<T> RunAsync<T>(string sessionId, Guid? userId, Func<Task<T>> work,
        CancellationToken cancellationToken = default);
    Task<T> MergeAsync<T>(string sessionId, Guid userId, Func<Task<T>> work);
    Task RecalculateAsync(Guid basketId, Func<Task> work);
}
