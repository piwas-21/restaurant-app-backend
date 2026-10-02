using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.DeliveryChannels.Management;
using RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetDeliveryChannelManagementSummaryQuery;

public sealed record GetDeliveryChannelManagementSummaryQuery : IQuery<DeliveryChannelManagementSummaryDto>;

public sealed class GetDeliveryChannelManagementSummaryQueryHandler(
    IOptions<DeliveryChannelManagementSettings> managementSettings,
    IOptions<DeliveryChannelSettings> channelSettings,
    ICurrentUserService currentUser,
    IDeliveryChannelManagementClient gateway)
    : IQueryHandler<GetDeliveryChannelManagementSummaryQuery, DeliveryChannelManagementSummaryDto>
{
    public Task<DeliveryChannelManagementSummaryDto> Handle(GetDeliveryChannelManagementSummaryQuery query,
        CancellationToken cancellationToken)
    {
        if (!managementSettings.Value.Enabled || !channelSettings.Value.Enabled)
            return Task.FromResult(NotProvisioned());

        var actorId = currentUser.UserId
            ?? throw new ForbiddenException("A signed-in tenant administrator is required.");
        return gateway.Send<DeliveryChannelManagementSummaryDto>(HttpMethod.Get,
            "api/tenant-management/uber/summary", actorId, null, cancellationToken);
    }

    private static DeliveryChannelManagementSummaryDto NotProvisioned() => new(
        "uber-eats", false, true, "notConnected", "unavailable", Guid.Empty, string.Empty, false, null,
        false, false, false, true, false, null, "IntegrationNotProvisioned",
        new(false, false, false, false, false, false, false), null);
}
