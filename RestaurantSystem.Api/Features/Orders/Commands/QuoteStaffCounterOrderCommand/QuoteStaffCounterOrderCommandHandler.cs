using System.Diagnostics.CodeAnalysis;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.QuoteStaffCounterOrderCommand;

public sealed class QuoteStaffCounterOrderCommandHandler
    : ICommandHandler<QuoteStaffCounterOrderCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IStaffCounterOrderBuilder _builder;
    private readonly IOrderMappingService _mapping;
    private readonly IOrderFidelityCoordinator _fidelity;

    public QuoteStaffCounterOrderCommandHandler(
        ApplicationDbContext context, IStaffCounterOrderBuilder builder,
        IOrderMappingService mapping, IOrderFidelityCoordinator fidelity)
    {
        _context = context;
        _builder = builder;
        _mapping = mapping;
        _fidelity = fidelity;
    }

    [SuppressMessage("Reliability", "S6966:Awaitable method should be used",
        Justification = "The quote aggregate is intentionally unsaved; the async mapper would attach it and issue database loads, while the builder has already populated every response navigation.")]
    public async Task<ApiResponse<OrderDto>> Handle(
        QuoteStaffCounterOrderCommand command, CancellationToken cancellationToken)
    {
        // OrderFactory's daily number lock requires a transaction. Rolling it back also rolls back
        // any customer-discount usage touched while pricing the quote, so this endpoint is read-only.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var build = await _builder.BuildAsync(command, releaseToKitchen: false, cancellationToken);
            await _fidelity.PreviewRedemptionAsync(
                build.Order, command.PointsToRedeem, build.CustomerUserId, cancellationToken);
            var quote = _mapping.MapToOrderDto(build.Order);
            return ApiResponse<OrderDto>.SuccessWithData(quote, "Counter order quote calculated");
        }
        finally
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }
}
