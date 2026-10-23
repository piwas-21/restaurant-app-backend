using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTableCheckoutFreezer(ApplicationDbContext context, ICustomerTableCheckoutAuthority authority,
    IAccountStripeCheckoutClient provider, IOptions<AccountCheckoutSettings> checkout,
    IOptions<CustomerHostedCheckoutSettings> hosted, TimeProvider clock) : ICustomerTableCheckoutFreezer
{
    public async Task<AccountCheckoutJournal> FreezeAsync(CustomerTablePaymentOwner owner,
        CustomerTablePaymentProofDto proof, CustomerTablePaymentBinding binding, string? participantToken,
        AccountPaymentAttempt attempt, CancellationToken cancellationToken)
    {
        await authority.RequireStartAsync(owner, proof, binding, participantToken, cancellationToken);
        var now = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds()).UtcDateTime;
        AccountCheckoutFreeze.RequireReserved(attempt, attempt.Version, now);
        var debt = await new AccountDebtSnapshotReader(context).ReadAsync(binding.ServiceSessionId, cancellationToken);
        var scope = attempt.Allocations.Select(row => new AccountDebtSegment(row.OrderId, row.OrderItemId,
            row.StartOrdinal, row.UnitCount, row.MinorPerUnit)).ToArray();
        if (scope.Length == 0 || debt.Money.Currency != binding.Currency
            || AccountDebtMath.Total(scope) != binding.AmountMinor)
            throw new ConflictException("The original contribution requires reconciliation.");
        AccountDebtMath.Subtract(debt.Debt.Outstanding, scope);
        var native = new NativeAccountCheckoutScope(proof.TenantId, proof.OperationId);
        var result = AccountCheckoutFreeze.Create(attempt, null, now, checkout.Value, provider, native,
            native.ReturnBase(hosted.Value.LinkOrigin, hosted.Value.TenantSlug, attempt.Id));
        // Revalidate authority after all asynchronous quote/debt reads while the visit remains locked.
        await authority.RecheckAsync(owner, proof, binding, participantToken, cancellationToken);
        return result;
    }

}
