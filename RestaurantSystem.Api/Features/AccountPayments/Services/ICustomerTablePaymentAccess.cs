namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed record CustomerTablePaymentOwner(Guid SessionId, Guid? CustomerId, string OwnerHash);

public interface ICustomerTablePaymentAccess
{
    CustomerTablePaymentOwner Capture(string sessionId, int schemaVersion, Guid tenantId);
    Task<string> RequireNewAsync(CustomerTablePaymentOwner owner, Guid tenantId, CancellationToken cancellationToken);
}
