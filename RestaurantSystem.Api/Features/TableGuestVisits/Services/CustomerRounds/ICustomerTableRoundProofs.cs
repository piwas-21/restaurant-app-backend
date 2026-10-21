using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public sealed record CustomerTableRoundOwner(Guid BasketSessionId, Guid? CustomerId, string OwnerHash);
public sealed record CustomerTableRoundBinding(Guid ServiceSessionId, Guid TableId, Guid GuestOperationId, Guid ParticipantId,
    string ParticipantTokenHash, long ExpectedAccountRevision, string BasketFingerprint,
    string Currency, string QuoteFingerprint);

public interface ICustomerTableRoundProofs
{
    CustomerTableRoundProofDto Create(CustomerTableRoundIntentDto request, CustomerTableRoundOwner owner,
        CustomerTableRoundBinding binding);
    CustomerTableRoundBinding Require(CustomerTableRoundProofDto proof, CustomerTableRoundOwner owner);
    void RequireInput(CustomerTableRoundIntentDto request, CustomerTableRoundOwner owner, CustomerTableRoundBinding binding);
}
