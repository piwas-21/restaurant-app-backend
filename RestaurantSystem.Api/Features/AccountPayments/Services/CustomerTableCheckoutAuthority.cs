using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTableCheckoutAuthority(ApplicationDbContext context, ICustomerTablePaymentAccess access,
    ITableGuestParticipantPaymentAuthorization participants, IGuestAccountPaymentPolicy policy) : ICustomerTableCheckoutAuthority
{
    public async Task RequireStartAsync(CustomerTablePaymentOwner owner, CustomerTablePaymentProofDto proof,
        CustomerTablePaymentBinding binding, string? participantToken, CancellationToken cancellationToken)
    {
        RequireToken(binding, participantToken);
        await RecheckAsync(owner, proof, binding, participantToken, cancellationToken);
        if (!await context.TableServiceSessions.AnyAsync(row => row.Id == binding.ServiceSessionId
            && row.TableId == binding.TableId, cancellationToken)) throw Unavailable();
        policy.RequireContribution(binding.AmountMinor, binding.Currency);
    }
    public async Task RecheckAsync(CustomerTablePaymentOwner owner, CustomerTablePaymentProofDto proof,
        CustomerTablePaymentBinding binding, string? participantToken, CancellationToken cancellationToken)
    {
        if (await access.RequireNewAsync(owner, proof.TenantId, cancellationToken) != binding.Currency
            || (await participants.AuthorizeActiveAsync(binding.ServiceSessionId, participantToken, cancellationToken)).ActorId != binding.ParticipantId)
            throw Unavailable();
    }
    public async Task<bool> MayOpenAsync(CustomerTableCheckoutIdentity original, CustomerTablePaymentProofDto proof,
        string? participantToken, CancellationToken cancellationToken)
    {
        try
        {
            RequireToken(original.Binding, participantToken);
            policy.RequireContribution(original.Binding.AmountMinor, original.Binding.Currency);
            await RecheckAsync(original.Owner, proof, original.Binding, participantToken, cancellationToken);
            return true;
        }
        catch (Exception error) when (error is NotFoundException or BadRequestException or ConflictException) { return false; }
    }
    private static void RequireToken(CustomerTablePaymentBinding binding, string? token)
    {
        if (!TableGuestCredentialCrypto.TryHashParticipantToken(token, out var hash) || hash != binding.ParticipantTokenHash)
            throw Unavailable();
    }
    private static NotFoundException Unavailable() => new("The original native contribution is unavailable.");
}
