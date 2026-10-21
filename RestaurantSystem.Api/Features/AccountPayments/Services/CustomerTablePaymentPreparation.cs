using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTablePaymentPreparation(ApplicationDbContext context, ICustomerTablePaymentAccess access,
    ITableGuestParticipantPaymentAuthorization participants, IGuestAccountPaymentPolicy policy,
    ICustomerTablePaymentProofs proofs, IOptions<AccountPaymentSettings> limits) : ICustomerTablePaymentPreparation
{
    public async Task<CustomerTablePaymentPreparedDto> PrepareAsync(string sessionId, string? participantToken,
        CustomerTablePaymentIntentDto request, CancellationToken cancellationToken)
    {
        var quote = QuoteRequest(request);
        if (quote.SelectedUnits.Count > limits.Value.MaximumSelectedUnits
            || !TableGuestCredentialCrypto.TryHashParticipantToken(participantToken, out var tokenHash)) throw Unsupported();
        var owner = access.Capture(sessionId, request.SchemaVersion, request.TenantId);
        var currency = await access.RequireNewAsync(owner, request.TenantId, cancellationToken);
        var participant = await participants.AuthorizeActiveAsync(request.ServiceSessionId, participantToken, cancellationToken);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Native payment preparation requires an isolated clean context.");
        CustomerTablePaymentPreparedDto prepared;
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken))
        {
            var account = await new AccountDebtSnapshotReader(context).ReadAsync(request.ServiceSessionId, cancellationToken);
            RequireAccount(request, account, currency);
            var segments = await AccountPaymentQuoteScope.SelectAsync(context, request.ServiceSessionId, quote, account, cancellationToken);
            RequireSegments(segments, limits.Value.MaximumScopeSegments);
            var amount = AccountDebtMath.Total(segments);
            if (amount > CustomerTablePaymentHashes.MaximumExactInteger) throw Unsupported();
            policy.RequireContribution(amount, currency);
            var capacity = await AccountCashRefundHistoryCapacityReader.ReadQuoteSizeAsync(context, request.ServiceSessionId, cancellationToken);
            if (!capacity.CanAdd(AccountCashRefundHistoryCapacityGrowth.ForQuote(segments.Count))) throw Unsupported();
            var allocations = AccountPaymentSnapshots.ToDtos(segments);
            var binding = new CustomerTablePaymentBinding(request.ServiceSessionId, account.Session.TableId!.Value,
                participant.ActorId, tokenHash, Guid.NewGuid(),
                account.Session.AccountRevision, amount, currency, CustomerBasketOperationHash.Payload(allocations));
            prepared = new(1, proofs.Create(request, owner, binding), request.ServiceSessionId,
                account.Session.AccountRevision, request.Mode, amount, currency, allocations);
            await transaction.CommitAsync(cancellationToken);
        }
        // Fresh authority checks deliberately run outside the repeatable-read snapshot.
        if (currency != await access.RequireNewAsync(owner, request.TenantId, cancellationToken)
            || participant != await participants.AuthorizeActiveAsync(request.ServiceSessionId, participantToken, cancellationToken))
            throw new NotFoundException("Customer table payment preparation is unavailable.");
        return prepared;
    }

    internal static CreateAccountPaymentQuoteRequest QuoteRequest(CustomerTablePaymentIntentDto request)
    {
        if (request.SchemaVersion != 1 || request.TenantId == Guid.Empty || request.ServiceSessionId == Guid.Empty
            || request.ExpectedAccountRevision > CustomerTablePaymentHashes.MaximumExactInteger)
            throw new BadRequestException("Review a supported current table account.");
        var quote = new CreateAccountPaymentQuoteRequest
        {
            OperationId = request.OperationId,
            ExpectedAccountRevision = request.ExpectedAccountRevision,
            Mode = request.Mode,
            PaymentMethod = PaymentMethod.OnlinePayment,
            SelectedUnits = request.SelectedUnits,
            AmountMinor = request.AmountMinor,
            EqualSharePlanId = request.EqualSharePlanId,
            EqualShareOrdinal = request.EqualShareOrdinal,
            CustomSharePlanId = request.CustomSharePlanId,
            CustomShareOrdinal = request.CustomShareOrdinal
        };
        AccountPaymentRequestRules.ValidateQuote(quote, allowOnlinePayment: true);
        if (quote.SelectedUnits.Count > CustomerTablePaymentHashes.MaximumSegments
            || quote.AmountMinor > CustomerTablePaymentHashes.MaximumExactInteger) throw Unsupported();
        return quote;
    }

    private static void RequireAccount(CustomerTablePaymentIntentDto request, AccountPaymentAccountSnapshot account, string currency)
    {
        if (account.Session.Status != TableServiceSessionStatus.Open || account.Session.ReleasedAt.HasValue
            || !account.Session.TableId.HasValue || account.Session.AccountRevision != request.ExpectedAccountRevision
            || account.Money.Currency != currency || account.Session.BillingAllocationVersion is < 0 or > 1) throw Unsupported();
    }

    private static void RequireSegments(IReadOnlyList<AccountDebtSegment> segments, int configuredMaximum)
    {
        if (segments.Count > Math.Min(configuredMaximum, CustomerTablePaymentHashes.MaximumSegments)
            || segments.Any(value => value.OrderId == Guid.Empty || value.OrderItemId == Guid.Empty
                || value.StartOrdinal < 1 || value.Count < 1 || value.MinorPerUnit <= 0
                || value.MinorPerUnit > CustomerTablePaymentHashes.MaximumExactInteger
                || value.TotalMinor > CustomerTablePaymentHashes.MaximumExactInteger)) throw Unsupported();
    }
    private static ConflictException Unsupported() => new("Refresh the table account before reviewing this contribution.");
}
