using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetStaffRoundOperationQuery;

public sealed record GetStaffRoundOperationQuery(Guid OperationId)
    : IQuery<ApiResponse<StaffRoundOperationLookupDto>>;

public sealed class GetStaffRoundOperationQueryHandler
    : IQueryHandler<GetStaffRoundOperationQuery, ApiResponse<StaffRoundOperationLookupDto>>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IStaffOrderOperationStore _operations;
    private readonly IOrderResponseProjector _responses;

    public GetStaffRoundOperationQueryHandler(
        ICurrentUserService currentUser, IStaffOrderOperationStore operations,
        IOrderResponseProjector responses)
    {
        _currentUser = currentUser;
        _operations = operations;
        _responses = responses;
    }

    public async Task<ApiResponse<StaffRoundOperationLookupDto>> Handle(
        GetStaffRoundOperationQuery query, CancellationToken cancellationToken)
    {
        var replay = await _operations.LookupAsync(
            query.OperationId, StaffOrderOperationKind.RoundCreate,
            _currentUser.UserId, cancellationToken);
        if (replay.Outcome != StaffOrderOperationReplayOutcome.Replay || replay.Order is null)
        {
            return ApiResponse<StaffRoundOperationLookupDto>.SuccessWithData(
                new StaffRoundOperationLookupDto
                {
                    OperationId = query.OperationId,
                    Status = StaffRoundOperationLookupStatus.Unknown
                },
                "Staff round operation status is unknown");
        }

        var order = await _responses.ProjectAsync(replay.Order, cancellationToken);
        return ApiResponse<StaffRoundOperationLookupDto>.SuccessWithData(
            new StaffRoundOperationLookupDto
            {
                OperationId = query.OperationId,
                Status = StaffRoundOperationLookupStatus.Committed,
                Order = order
            },
            "Staff round operation committed");
    }
}
