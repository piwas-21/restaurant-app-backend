using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public sealed class CustomerTableRoundExecution(ICustomerTableRoundAccess access, ICustomerTableRoundProofs proofs,
    ICustomerTableRoundQuotes quotes, ITableGuestRoundOperationStore rounds, ICustomerTableRoundWriter writer,
    IBasketService baskets, ICustomerTableRoundReceipts receipts) : ICustomerTableRoundExecution
{
    public async Task<CustomerTableRoundPreparedDto> PrepareAsync(string sessionId, CustomerTableRoundOwner owner,
        string? participantToken, CustomerTableRoundIntentDto request, CancellationToken cancellationToken)
    {
        await receipts.RequireUnusedAsync(request.OperationId, cancellationToken);
        var tokenHash = TokenHash(participantToken);
        // A separate server-generated ID keeps the legacy round namespace private to this proof.
        var guestOperationId = Guid.NewGuid();
        var round = Round(request, owner, guestOperationId, tokenHash);
        var prepared = await rounds.PrepareUnderLockAsync(round, cancellationToken);
        if (prepared.Replay is not null || prepared.Participant is not { } participant) throw Unavailable();
        await access.LockCustomerAsync(owner, cancellationToken);
        var review = await quotes.ReviewAsync(sessionId, owner, request, prepared.Session, cancellationToken);
        var binding = new CustomerTableRoundBinding(request.ServiceSessionId, prepared.Session.TableId.GetValueOrDefault(),
            guestOperationId, participant.Id, tokenHash, request.ExpectedAccountRevision,
            request.ExpectedPurchaseFingerprint, review.Context.Currency, review.Quote.QuoteFingerprint);
        await access.RevalidateAsync(owner, binding, false, cancellationToken);
        RequireOwner(sessionId, request.SchemaVersion, request.TenantId, owner);
        return new CustomerTableRoundPreparedDto(1, proofs.Create(request, owner, binding), review.Quote, review.TableLabel);
    }
    public async Task<CustomerTableRoundDecision> SubmitAsync(string sessionId, CustomerTableRoundOwner owner, CustomerTableRoundProofDto proof,
        CustomerTableRoundBinding binding, CustomerTableRoundIntentDto request, string? token, CancellationToken cancellationToken)
    {
        if (TokenHash(token) != binding.ParticipantTokenHash) throw Unavailable();
        var round = Round(request, owner, binding.GuestOperationId, binding.ParticipantTokenHash);
        var prepared = await rounds.PrepareUnderLockAsync(round, cancellationToken);
        await access.LockCustomerAsync(owner, cancellationToken);
        RequireOwner(sessionId, proof.SchemaVersion, proof.TenantId, owner);
        if (prepared.Replay is not null)
            return new(await receipts.ReadAsync(owner, proof, binding, cancellationToken) ?? throw Unavailable(), null);
        if (prepared.Participant?.Id != binding.ParticipantId || prepared.Session.TableId != binding.TableId) throw Unavailable();
        var review = await quotes.ReviewAsync(sessionId, owner, request, prepared.Session, cancellationToken);
        if (review.Quote.QuoteFingerprint != binding.QuoteFingerprint || review.Context.Currency != binding.Currency)
            throw new ConflictException("The reviewed table round changed. Review the current price before sending.");
        var order = await writer.CreateAsync(review, round, prepared, cancellationToken);
        await baskets.ClearBasketAsync(sessionId);
        var result = await receipts.SaveAsync(owner, proof, binding, order.Id, cancellationToken);
        await access.RevalidateAsync(owner, binding, true, cancellationToken);
        RequireOwner(sessionId, proof.SchemaVersion, proof.TenantId, owner);
        var durable = await receipts.ReadAsync(owner, proof, binding, cancellationToken);
        if (durable != result) throw Unavailable();
        return new(result, order);
    }
    private void RequireOwner(string sessionId, int schemaVersion, Guid tenantId, CustomerTableRoundOwner owner) =>
        access.RequireOriginal(sessionId, schemaVersion, tenantId, owner);
    private static string TokenHash(string? token) => TableGuestCredentialCrypto.TryHashParticipantToken(token, out var hash)
        ? hash : throw Unavailable();
    private static TableGuestRoundContext Round(CustomerTableRoundIntentDto request, CustomerTableRoundOwner owner,
        Guid guestOperationId, string tokenHash) => new(request.ServiceSessionId, guestOperationId,
            request.ExpectedAccountRevision, tokenHash, TableGuestCredentialCrypto.HashBasketSession(owner.BasketSessionId.ToString("D")),
            request.ExpectedPurchaseFingerprint.ToUpperInvariant());
    private static NotFoundException Unavailable() => new("Customer table round operation unavailable.");
}
