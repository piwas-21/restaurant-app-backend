using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCurrentStaffTicketQuery;

public sealed record GetCurrentStaffTicketQuery(Guid OrderId) : IQuery<ApiResponse<IReadOnlyList<OrderDto>>>;

public sealed class GetCurrentStaffTicketQueryHandler(
    ICurrentUserService currentUser, ICurrentStaffTicketReader tickets)
    : IQueryHandler<GetCurrentStaffTicketQuery, ApiResponse<IReadOnlyList<OrderDto>>>
{
    public async Task<ApiResponse<IReadOnlyList<OrderDto>>> Handle(
        GetCurrentStaffTicketQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsStaff) throw new ForbiddenException("The current ticket is available only to staff.");
        return ApiResponse<IReadOnlyList<OrderDto>>.SuccessWithData(
            await tickets.ReadAsync(request.OrderId, cancellationToken));
    }
}
