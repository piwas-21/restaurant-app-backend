using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.User.Dtos;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.User.Queries.GetCustomerAccountDeletionQuery;

public sealed record GetCustomerAccountDeletionQuery : IQuery<ApiResponse<CustomerAccountDeletionDto>>;

public sealed class GetCustomerAccountDeletionQueryHandler(ApplicationDbContext context, ICurrentUserService currentUser)
    : IQueryHandler<GetCustomerAccountDeletionQuery, ApiResponse<CustomerAccountDeletionDto>>
{
    public async Task<ApiResponse<CustomerAccountDeletionDto>> Handle(GetCustomerAccountDeletionQuery query,
        CancellationToken cancellationToken)
    {
        var account = await CustomerAccountAccess.Current(context, currentUser).AsNoTracking()
            .Select(user => new { user.DeletionScheduledAt }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Customer account unavailable.");
        var state = account.DeletionScheduledAt.HasValue ? "scheduled" : "not-requested";
        return ApiResponse<CustomerAccountDeletionDto>.SuccessWithData(new(1, state, account.DeletionScheduledAt));
    }
}
