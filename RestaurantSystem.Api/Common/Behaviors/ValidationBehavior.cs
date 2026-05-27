using FluentValidation;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Common.Behaviors
{
    /// <summary>
    /// Pipeline behavior that runs every <see cref="IValidator{TRequest}"/>
    /// registered for the incoming command or query before the handler executes.
    /// Aggregates failures into a single <see cref="BadRequestException"/>
    /// (which the global exception middleware maps to HTTP 400). When no
    /// validator is registered for the request type the behavior is a no-op.
    /// </summary>
    public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators;

        public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        {
            _validators = validators;
        }

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            if (!_validators.Any())
            {
                return await next();
            }

            var context = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

            var failures = results
                .Where(r => !r.IsValid)
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Count > 0)
            {
                var message = string.Join("; ", failures.Select(f => f.ErrorMessage));
                throw new BadRequestException(message);
            }

            return await next();
        }
    }
}
