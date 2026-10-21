using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public interface ICustomerTableRoundService
{
    Task<CustomerTableRoundPreparedDto> PrepareAsync(string sessionId, string? participantToken,
        CustomerTableRoundIntentDto request, CancellationToken cancellationToken);
    Task<CustomerTableRoundResolutionDto> SubmitAsync(string sessionId, string? participantToken,
        CustomerTableRoundIntentDto request, CancellationToken cancellationToken);
    Task<CustomerTableRoundResolutionDto> ResolveAsync(string sessionId, CustomerTableRoundProofDto proof,
        bool cancelAttempt, CancellationToken cancellationToken);
}
