using FluentValidation;
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

        public async Task<TResult> SendCommand<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default)
       where TCommand : ICommand<TResult>
        {
            var validator = _serviceProvider.GetService<IValidator<TCommand>>();
            if (validator != null)
                await validator.ValidateAndThrowAsync(command, cancellationToken);

            var handlerType = typeof(ICommandHandler<TCommand, TResult>);
            var handler = _serviceProvider.GetService(handlerType) as ICommandHandler<TCommand, TResult>;

            if (handler == null)
                throw new Exception($"No command handler registered for {typeof(TCommand).Name}");

            return await handler.Handle(command, cancellationToken);
        }

        public async Task<TResult> SendCommand<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
        {
            var commandType = command.GetType();

            var validatorType = typeof(IValidator<>).MakeGenericType(commandType);
            if (_serviceProvider.GetService(validatorType) is IValidator validator)
            {
                var context = new ValidationContext<object>(command);
                var result = await validator.ValidateAsync(context, cancellationToken);
                if (!result.IsValid)
                    throw new ValidationException(result.Errors);
            }

            var handlerType = typeof(ICommandHandler<,>).MakeGenericType(commandType, typeof(TResult));
            dynamic handler = _serviceProvider.GetService(handlerType)!;

            if (handler == null)
                throw new Exception($"No command handler registered for {commandType.Name}");

            return await handler.Handle((dynamic)command, cancellationToken);
        }


        // Special case for commands without a return value
        public async Task SendCommand(ICommand<Unit> command, CancellationToken cancellationToken = default)
        {
            await SendCommand<Unit>(command, cancellationToken);
        }

        // Send a query
        public async Task<TResult> SendQuery<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default)
            where TQuery : IQuery<TResult>
        {
            var handlerType = typeof(IQueryHandler<TQuery, TResult>);
            var handler = _serviceProvider.GetService(handlerType) as IQueryHandler<TQuery, TResult>;

            if (handler == null)
                throw new Exception($"No query handler registered for {typeof(TQuery).Name}");

            return await handler.Handle(query, cancellationToken);
        }

        // Generic query method that infers the result type
        public async Task<TResult> SendQuery<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            var queryType = query.GetType();
            var handlerType = typeof(IQueryHandler<,>).MakeGenericType(queryType, typeof(TResult));
            dynamic? handler = _serviceProvider.GetService(handlerType);

            if (handler == null)
                throw new Exception($"No query handler registered for {queryType.Name}");

            return await handler.Handle((dynamic)query, cancellationToken);
        }


    }

    // Unit type for commands that don't return a value
    public struct Unit
    {
        public static Unit Value => new Unit();
    }
}
