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
        private readonly IValidator<TRequest>[] _validators;

        public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        {
            // Materialize once so we can do O(1) length checks and avoid
            // multiple enumeration of the DI-provided IEnumerable.
            _validators = validators as IValidator<TRequest>[] ?? validators.ToArray();
        }

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            // Fast path: no validators registered for this request type.
            if (_validators.Length == 0)
            {
                return await next();
            }

            var context = new ValidationContext<TRequest>(request);

            // Fast path: a single validator avoids Task.WhenAll, LINQ Select,
            // and the extra task allocations the multi-validator path needs.
            if (_validators.Length == 1)
            {
                var result = await _validators[0].ValidateAsync(context, cancellationToken);
                if (!result.IsValid)
                {
                    var singleMessage = string.Join(
                        "; ",
                        result.Errors.Where(f => f is not null).Select(f => f.ErrorMessage));
                    throw new BadRequestException(singleMessage);
                }

                return await next();
            }

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
