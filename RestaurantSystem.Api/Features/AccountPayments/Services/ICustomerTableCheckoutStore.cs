using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public enum CustomerTableCheckoutAction { Start, Recover, Stop }
public sealed record CustomerTableCheckoutIdentity(CustomerTablePaymentOwner Owner,
    CustomerTablePaymentBinding Binding, Guid? AttemptId, bool HasJournal);

public interface ICustomerTableCheckoutStore
{
    Task<CustomerTableCheckoutIdentity> RunAsync(string sessionId, CustomerTablePaymentProofDto proof,
        string? participantToken, CustomerTableCheckoutAction action, CancellationToken cancellationToken);
}
