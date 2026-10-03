using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountPaymentQuoteService
{
    Task<AccountPaymentOperationDto> CreateQuoteAsync(
        Guid sessionId, CreateAccountPaymentQuoteRequest request, CancellationToken cancellationToken);

    Task<AccountPaymentOperationDto> CreateGuestQuoteAsync(
        Guid sessionId, string? participantCredential, CreateAccountPaymentQuoteRequest request,
        CancellationToken cancellationToken);
}
