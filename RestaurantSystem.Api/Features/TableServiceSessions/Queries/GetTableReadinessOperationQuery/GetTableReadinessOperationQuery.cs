using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.MarkTableReadyCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetTableReadinessOperationQuery;

public sealed record GetTableReadinessOperationQuery(Guid TableId, Guid OperationId)
    : IQuery<ApiResponse<TableReadinessOperationDto>>;

public sealed class GetTableReadinessOperationQueryHandler(
    ApplicationDbContext context, ICurrentUserService currentUser)
    : IQueryHandler<GetTableReadinessOperationQuery, ApiResponse<TableReadinessOperationDto>>
{
    public async Task<ApiResponse<TableReadinessOperationDto>> Handle(
        GetTableReadinessOperationQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue
            || currentUser.Role is not (UserRole.Admin or UserRole.Cashier or UserRole.Server))
            return ApiResponse<TableReadinessOperationDto>.FailureWithCode(
                "An authenticated table-service staff member is required.", ErrorCodes.TableReadinessStaffRequired);

        var operation = await context.TableReadyOperations.AsNoTracking()
            .SingleOrDefaultAsync(value => value.TableId == query.TableId
                && value.OperationId == query.OperationId
                && value.ActorUserId == currentUser.UserId.Value && value.ActorRole == currentUser.Role,
                cancellationToken);
        return operation is null
            ? ApiResponse<TableReadinessOperationDto>.FailureWithCode(
                "This table readiness operation is unavailable.", ErrorCodes.TableReadinessOperationNotFound)
            : TableReadinessOperationResponses.ToResponse(operation);
    }
}
