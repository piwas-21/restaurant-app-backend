using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public interface ICustomerTableRoundExecution
{
    Task<CustomerTableRoundPreparedDto> PrepareAsync(string sessionId, CustomerTableRoundOwner owner,
        string? participantToken, CustomerTableRoundIntentDto request, CancellationToken cancellationToken);
    Task<CustomerTableRoundDecision> SubmitAsync(string sessionId, CustomerTableRoundOwner owner, CustomerTableRoundProofDto proof,
        CustomerTableRoundBinding binding, CustomerTableRoundIntentDto request, string? token, CancellationToken cancellationToken);
}

public sealed record CustomerTableRoundDecision(CustomerTableRoundResolutionDto Resolution, Order? Order);
