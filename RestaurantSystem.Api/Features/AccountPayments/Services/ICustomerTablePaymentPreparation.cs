using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface ICustomerTablePaymentPreparation
{
    Task<CustomerTablePaymentPreparedDto> PrepareAsync(string sessionId, string? participantToken,
        CustomerTablePaymentIntentDto request, CancellationToken cancellationToken);
}
