namespace RestaurantSystem.Api.Abstraction.Messaging
{
    /// <summary>
    /// Delegate representing the next step in the mediator pipeline (either the
    /// next behavior or the final handler invocation).
    /// </summary>
    public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

    /// <summary>
    /// Cross-cutting behavior wrapped around command/query handler dispatch by
    /// <see cref="RestaurantSystem.Api.Common.CustomMediator"/>. Behaviors run
    /// in the order they are registered in DI; call the supplied
    /// <c>next</c> delegate to continue the pipeline, or short-circuit by
    /// throwing.
    /// </summary>
    public interface IPipelineBehavior<in TRequest, TResponse>
        where TRequest : notnull
    {
        /// <summary>
        /// Invokes the behavior for the supplied <paramref name="request"/>.
        /// Call <paramref name="next"/> to continue the pipeline, or throw to
        /// short-circuit.
        /// </summary>
        Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken);
    }
}
