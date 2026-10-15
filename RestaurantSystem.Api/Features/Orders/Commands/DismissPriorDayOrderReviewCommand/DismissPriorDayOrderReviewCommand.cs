using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.DismissPriorDayOrderReviewCommand;

public sealed record DismissPriorDayOrderReviewCommand(Guid OrderId) : ICommand<ApiResponse<bool>>;

public sealed class DismissPriorDayOrderReviewCommandHandler(
    ApplicationDbContext context,
    ICurrentUserService currentUser,
    ITenantClock clock)
    : ICommandHandler<DismissPriorDayOrderReviewCommand, ApiResponse<bool>>
{
    public async Task<ApiResponse<bool>> Handle(
        DismissPriorDayOrderReviewCommand request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAdmin && currentUser.Role != UserRole.Cashier)
            throw new ForbiddenException("Only an admin or cashier may dismiss prior-day review.");

        var order = await context.Orders.FirstOrDefaultAsync(
            candidate => candidate.Id == request.OrderId && !candidate.IsDeleted, cancellationToken);
        if (order is null)
            throw new NotFoundException("Order not found.");

        if (order.BusinessDate >= DateOnly.FromDateTime(clock.Now.Date))
            throw new BadRequestException("Only a prior-day order can be dismissed from review.");

        if (!order.PriorDayReviewDismissedAt.HasValue)
        {
            order.PriorDayReviewDismissedAt = DateTime.UtcNow;
            order.PriorDayReviewDismissedBy = currentUser.GetAuditIdentifier();
            await context.SaveChangesAsync(cancellationToken);
        }

        return ApiResponse<bool>.SuccessWithData(true);
    }
}
