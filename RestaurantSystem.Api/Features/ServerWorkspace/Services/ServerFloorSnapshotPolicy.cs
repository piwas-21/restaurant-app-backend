using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

public sealed class ServerFloorSnapshotPolicy(
    ITenantFeatures features,
    IAccountPaymentActorResolver paymentActors) : IServerFloorSnapshotPolicy
{
    public bool TableVisitReadinessEnabled => features.TableVisitReadinessV1;
    public bool CanStartCollection => paymentActors.CanStartCollection;
}
