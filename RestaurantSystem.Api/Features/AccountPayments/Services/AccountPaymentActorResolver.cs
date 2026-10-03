using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed record AccountPaymentActor(
    Guid ActorId, AccountPaymentActorKind Kind, string AuditIdentifier, UserRole? Role = null);

public interface IAccountPaymentActorResolver
{
    AccountPaymentActor ResolveStaffActor();
    bool CanStartCollection { get; }
    void RequireNewCollection();
}

public sealed class AccountPaymentActorResolver(
    ICurrentUserService currentUser, ITenantFeatures features, ITenantModules modules) : IAccountPaymentActorResolver
{
    private bool HasStaffIdentity => currentUser.IsAuthenticated && !currentUser.IsApiToken
        && currentUser.UserId is Guid actorId && actorId != Guid.Empty
        && currentUser.Role is UserRole.Admin or UserRole.Cashier or UserRole.Server;

    public bool CanStartCollection => HasStaffIdentity && features.TableAccountPaymentsV1
        && (currentUser.Role switch
        {
            UserRole.Admin or UserRole.Cashier =>
                modules.IsEnabled(ModuleIds.Server) || modules.IsEnabled(ModuleIds.Cashier),
            UserRole.Server => features.ServerAccountCollectionV1 && modules.IsEnabled(ModuleIds.Server),
            _ => false
        });

    public void RequireNewCollection()
    {
        ResolveStaffActor();
        if (!CanStartCollection)
            throw new NotFoundException("New table account collection is not available.");
    }

    // Resolve identity separately so the original owner can finish or release accepted work after opt-out.
    public AccountPaymentActor ResolveStaffActor()
    {
        if (!currentUser.IsAuthenticated || currentUser.IsApiToken
            || currentUser.UserId is not Guid actorId || actorId == Guid.Empty
            || currentUser.Role is not (UserRole.Admin or UserRole.Cashier or UserRole.Server))
        {
            throw new ForbiddenException("Only authenticated table-service staff may access a payment operation.");
        }

        return new AccountPaymentActor(
            actorId, AccountPaymentActorKind.Staff, currentUser.GetAuditIdentifier(), currentUser.Role);
    }
}
