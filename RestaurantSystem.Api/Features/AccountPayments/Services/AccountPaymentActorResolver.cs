using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed record AccountPaymentActor(Guid ActorId, AccountPaymentActorKind Kind, string AuditIdentifier);

public interface IAccountPaymentActorResolver
{
    AccountPaymentActor ResolveStaffActor();
}

public sealed class AccountPaymentActorResolver(ICurrentUserService currentUser) : IAccountPaymentActorResolver
{
    public AccountPaymentActor ResolveStaffActor()
    {
        if (!currentUser.IsAuthenticated || currentUser.IsApiToken
            || currentUser.UserId is not Guid actorId || actorId == Guid.Empty
            || currentUser.Role is not (UserRole.Admin or UserRole.Cashier))
        {
            throw new ForbiddenException("Only an authenticated admin or cashier may collect a table account.");
        }

        return new AccountPaymentActor(
            actorId, AccountPaymentActorKind.Staff, currentUser.GetAuditIdentifier());
    }
}
