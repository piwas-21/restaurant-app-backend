using RestaurantSystem.Api.Abstraction.Messaging;

namespace RestaurantSystem.Api.Common
{
    public class CustomMediator
    {
        private readonly IServiceProvider _serviceProvider;

        public CustomMediator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Task<TResult> SendCommand<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResult>
        {
            var handlerType = typeof(ICommandHandler<TCommand, TResult>);
            var handler = _serviceProvider.GetService(handlerType) as ICommandHandler<TCommand, TResult>;

            if (handler == null)
                throw new Exception($"No command handler registered for {typeof(TCommand).Name}");

            return InvokePipeline<TCommand, TResult>(
                command,
                () => handler.Handle(command, cancellationToken),
                cancellationToken);
        }

        public Task<TResult> SendCommand<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
        {
            var commandType = command.GetType();
            var handlerType = typeof(ICommandHandler<,>).MakeGenericType(commandType, typeof(TResult));
            dynamic handler = _serviceProvider.GetService(handlerType)!;

            if (handler == null)
                throw new Exception($"No command handler registered for {commandType.Name}");

            return InvokePipelineNonGeneric<TResult>(
                command,
                commandType,
                () => (Task<TResult>)handler.Handle((dynamic)command, cancellationToken),
                cancellationToken);
        }

        // Special case for commands without a return value
        public async Task SendCommand(ICommand<Unit> command, CancellationToken cancellationToken = default)
        {
            await SendCommand<Unit>(command, cancellationToken);
        }

        // Send a query (strongly-typed)
        public Task<TResult> SendQuery<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default)
            where TQuery : IQuery<TResult>
        {
            var handlerType = typeof(IQueryHandler<TQuery, TResult>);
            var handler = _serviceProvider.GetService(handlerType) as IQueryHandler<TQuery, TResult>;

            if (handler == null)
                throw new Exception($"No query handler registered for {typeof(TQuery).Name}");

            return InvokePipeline<TQuery, TResult>(
                query,
                () => handler.Handle(query, cancellationToken),
                cancellationToken);
        }

        // Generic query method that infers the result type
        public Task<TResult> SendQuery<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            var queryType = query.GetType();
            var handlerType = typeof(IQueryHandler<,>).MakeGenericType(queryType, typeof(TResult));
            dynamic? handler = _serviceProvider.GetService(handlerType);

            if (handler == null)
                throw new Exception($"No query handler registered for {queryType.Name}");

            return InvokePipelineNonGeneric<TResult>(
                query,
                queryType,
                () => (Task<TResult>)handler.Handle((dynamic)query, cancellationToken),
                cancellationToken);
        }

        private Task<TResult> InvokePipeline<TRequest, TResult>(
            TRequest request,
            Func<Task<TResult>> handlerInvocation,
            CancellationToken cancellationToken)
            where TRequest : notnull
        {
            var behaviors = _serviceProvider
                .GetServices(typeof(IPipelineBehavior<TRequest, TResult>))
                .Cast<IPipelineBehavior<TRequest, TResult>>()
                .Reverse()
                .ToList();

            RequestHandlerDelegate<TResult> pipeline = () => handlerInvocation();

            foreach (var behavior in behaviors)
            {
                var next = pipeline;
                pipeline = () => behavior.Handle(request, next, cancellationToken);
            }

            return pipeline();
        }

        private Task<TResult> InvokePipelineNonGeneric<TResult>(
            object request,
            Type requestType,
            Func<Task<TResult>> handlerInvocation,
            CancellationToken cancellationToken)
        {
            var behaviorInterface = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResult));
            var behaviors = _serviceProvider.GetServices(behaviorInterface)
                .Where(b => b is not null)
                .Cast<object>()
                .Reverse()
                .ToList();

            RequestHandlerDelegate<TResult> pipeline = () => handlerInvocation();

            foreach (var behavior in behaviors)
            {
                var next = pipeline;
                var handleMethod = behaviorInterface.GetMethod(nameof(IPipelineBehavior<object, TResult>.Handle))!;
                pipeline = () => (Task<TResult>)handleMethod.Invoke(behavior, new object[] { request, next, cancellationToken })!;
            }

            return pipeline();
        }
    }

    // Unit type for commands that don't return a value
    public struct Unit
    {
        public static Unit Value => new Unit();
    }
}
