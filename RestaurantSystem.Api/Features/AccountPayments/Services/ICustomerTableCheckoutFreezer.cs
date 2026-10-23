using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface ICustomerTableCheckoutFreezer
{
    Task<AccountCheckoutJournal> FreezeAsync(CustomerTablePaymentOwner owner, CustomerTablePaymentProofDto proof,
        CustomerTablePaymentBinding binding, string? participantToken, AccountPaymentAttempt attempt, CancellationToken cancellationToken);
}
