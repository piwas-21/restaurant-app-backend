using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Interfaces;

public interface IAccountPaymentReceiptReader
{
    Task<AccountPaymentReceiptDto> ReadAsync(
        Guid attemptId, string? receiptCredential, CancellationToken cancellationToken);
}
