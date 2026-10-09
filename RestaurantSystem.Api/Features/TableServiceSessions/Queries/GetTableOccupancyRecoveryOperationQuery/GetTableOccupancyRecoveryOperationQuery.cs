using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetTableOccupancyRecoveryOperationQuery;

public sealed record GetTableOccupancyRecoveryOperationQuery(Guid TableId, Guid OperationId)
    : IQuery<ApiResponse<TableOccupancyRecoveryOperationDto>>;

public sealed class GetTableOccupancyRecoveryOperationQueryHandler(
    ApplicationDbContext context, ICurrentUserService currentUser)
    : IQueryHandler<GetTableOccupancyRecoveryOperationQuery, ApiResponse<TableOccupancyRecoveryOperationDto>>
{
    public async Task<ApiResponse<TableOccupancyRecoveryOperationDto>> Handle(
        GetTableOccupancyRecoveryOperationQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue
            || currentUser.Role is not (UserRole.Admin or UserRole.Cashier or UserRole.Server))
            return ApiResponse<TableOccupancyRecoveryOperationDto>.FailureWithCode(
                "An authenticated table-service staff member is required.",
                ErrorCodes.TableReadinessStaffRequired);

        var operation = await context.TableOccupancyRecoveryOperations.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == query.OperationId && value.TableId == query.TableId
                && value.ActorUserId == currentUser.UserId.Value && value.ActorRole == currentUser.Role,
                cancellationToken);
        if (operation is null)
            return ApiResponse<TableOccupancyRecoveryOperationDto>.FailureWithCode(
                "Table occupancy recovery operation was not found.",
                ErrorCodes.TableOccupancyRecoveryOperationNotFound);

        var dispositions = await context.TableOccupancyRecoveryDispositions.AsNoTracking()
            .Where(value => value.OperationId == operation.Id)
            .ToListAsync(cancellationToken);
        return ApiResponse<TableOccupancyRecoveryOperationDto>.SuccessWithData(
            TableOccupancyRecoveryResponses.ToDto(operation, dispositions),
            "Table occupancy recovery result retrieved");
    }
}
