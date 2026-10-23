using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTablePaymentAcceptance(ApplicationDbContext context, ICustomerTablePaymentAccess access,
    ITableGuestParticipantPaymentAuthorization participants, IAccountPaymentQuoteService quotes,
    IAccountPaymentReservationService reservations) : ICustomerTablePaymentAcceptance
{
    public async Task<Guid> AcceptAsync(CustomerTablePaymentOwner owner, CustomerTablePaymentProofDto proof,
        CustomerTablePaymentBinding binding, CustomerTablePaymentIntentDto intent, string? participantToken,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new ConflictException("Native contribution acceptance requires the decision transaction.");
        if (!TableGuestCredentialCrypto.TryHashParticipantToken(participantToken, out var tokenHash)
            || tokenHash != binding.ParticipantTokenHash) throw Unavailable();
        await RequireCurrentAsync(owner, proof, binding, participantToken, cancellationToken);
        var request = CustomerTablePaymentPreparation.QuoteRequest(intent with { OperationId = binding.GuestOperationId });
        var quote = await quotes.CreateGuestQuoteAsync(binding.ServiceSessionId, participantToken, request, cancellationToken);
        if (quote.ExpectedAccountRevision != binding.AccountRevision || quote.AmountMinor != binding.AmountMinor
            || quote.Currency != binding.Currency || CustomerBasketOperationHash.Payload(quote.Allocations) != binding.ScopeHash)
            throw new ConflictException("The prepared contribution changed. Review the account before paying.");
        await reservations.ReserveGuestAsync(binding.ServiceSessionId, binding.GuestOperationId, participantToken,
            new() { ExpectedVersion = quote.Version, ExpectedAccountRevision = binding.AccountRevision }, cancellationToken);
        await RequireCurrentAsync(owner, proof, binding, participantToken, cancellationToken);
        return await context.AccountPaymentAttempts.Where(row => row.OperationId == binding.GuestOperationId)
            .Select(row => row.Id).SingleAsync(cancellationToken);
    }

    private async Task RequireCurrentAsync(CustomerTablePaymentOwner owner, CustomerTablePaymentProofDto proof,
        CustomerTablePaymentBinding binding, string? token, CancellationToken cancellationToken)
    {
        var currency = await access.RequireNewAsync(owner, proof.TenantId, cancellationToken);
        var participant = await participants.AuthorizeActiveAsync(binding.ServiceSessionId, token, cancellationToken);
        if (currency != binding.Currency || participant.ActorId != binding.ParticipantId
            || !await context.TableServiceSessions.AnyAsync(row => row.Id == binding.ServiceSessionId
                && row.TableId == binding.TableId, cancellationToken)) throw Unavailable();
    }
    private static NotFoundException Unavailable() => new("Customer table payment operation unavailable.");
}
