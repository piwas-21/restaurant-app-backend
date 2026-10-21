using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface ICustomerTablePaymentAccountReader
{
    Task<CustomerTablePaymentAccountDto> ReadAsync(string sessionId, string? participantToken,
        int? schemaVersion, Guid tenantId, Guid serviceSessionId, CancellationToken cancellationToken);
}
