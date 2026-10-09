using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class AccountPaymentQuoteService(
    ApplicationDbContext context,
    IAccountPaymentActorResolver actors,
    ITableGuestParticipantPaymentAuthorization guestAuthorization,
    IGuestAccountPaymentPolicy guestPolicy,
    ITenantFeatures features,
    IOptions<AccountPaymentSettings> options,
    TimeProvider timeProvider) : IAccountPaymentQuoteService
{
    public async Task<AccountPaymentOperationDto> CreateQuoteAsync(
        Guid sessionId, CreateAccountPaymentQuoteRequest request, CancellationToken cancellationToken)
    {
        if (!features.TableAccountPaymentsV1)
            throw new NotFoundException("Table account payments are not enabled.");
        actors.RequireNewCollection();
        return await CreateQuoteCoreAsync(sessionId, null, request, guest: false, cancellationToken);
    }

    public async Task<AccountPaymentOperationDto> CreateGuestQuoteAsync(
        Guid sessionId, string? participantCredential, CreateAccountPaymentQuoteRequest request,
        CancellationToken cancellationToken)
    {
        guestPolicy.RequireNewPayment();
        return await CreateQuoteCoreAsync(sessionId, participantCredential, request, guest: true, cancellationToken);
    }

    private async Task<AccountPaymentOperationDto> CreateQuoteCoreAsync(
        Guid sessionId, string? participantCredential, CreateAccountPaymentQuoteRequest request,
        bool guest, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var tipMinor = AccountPaymentRequestRules.EffectiveTipMinor(request);
        ValidateQuoteRequest(sessionId, request, guest, tipMinor, settings);
        var staffActor = guest ? null : actors.ResolveStaffActor();
        var hash = AccountPaymentRequestRules.QuoteHash(sessionId, request, allowOnlinePayment: guest);
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        try
        {
            var session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken)
                ?? throw new NotFoundException("Table account was not found.");
            var actor = await ResolveActorAsync(session, participantCredential, guest, staffActor, cancellationToken);
            await AccountPaymentOperationKeyLock.AcquireAsync(context, request.OperationId, cancellationToken);
            var replay = await FindExistingQuoteAsync(sessionId, request.OperationId, actor, hash, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            RequireOpen(session);
            AccountPaymentRequestRules.RequireCurrentRevision(session.AccountRevision, request.ExpectedAccountRevision);
            var account = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, cancellationToken);
            var segments = await SelectSegmentsAsync(sessionId, request, account, cancellationToken);
            RequireSegmentLimit(segments, settings.MaximumScopeSegments);
            var amount = AccountDebtMath.Total(segments);
            if (guest) guestPolicy.RequireContribution(amount, account.Money.Currency);
            var cashSettlement = ResolveCashSettlement(
                amount, tipMinor, account.Money.Currency, request.PaymentMethod, settings);
            var capacity = await AccountCashRefundHistoryCapacityReader.ReadQuoteSizeAsync(
                context, sessionId, cancellationToken);
            RequireCapacityForQuote(capacity, segments.Count);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var quoteExpires = now.AddMinutes(settings.QuoteLifetimeMinutes);
            var attempt = CreateAttempt(
                new QuoteIdentity(sessionId, request, actor, hash),
                new QuoteFinancials(amount, tipMinor, account.Money.Currency, quoteExpires, now, segments, cashSettlement));
            context.AccountPaymentAttempts.Add(attempt);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AccountPaymentSnapshots.ToOperation(attempt);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConflictException("The account changed while the payment quote was created.", exception);
        }
        catch (DbUpdateException exception) when (AccountPaymentWriteErrors.IsOperationKeyConflict(exception))
        {
            throw AccountPaymentWriteErrors.OperationKeyConflict(exception);
        }
    }

    private static void ValidateQuoteRequest(
        Guid sessionId, CreateAccountPaymentQuoteRequest request, bool guest, long tipMinor,
        AccountPaymentSettings settings)
    {
        if (sessionId == Guid.Empty) throw new BadRequestException("A table visit is required.");
        AccountPaymentRequestRules.ValidateQuote(request, allowOnlinePayment: guest);
        if (guest && request.PaymentMethod != PaymentMethod.OnlinePayment)
            throw new BadRequestException("Guest account payments support online payment only.");
        if (tipMinor > 0 && (guest || request.PaymentMethod == PaymentMethod.OnlinePayment))
            throw new BadRequestException("Tips are available only for staff-collected cash or card payments.");
        if (request.SelectedUnits.Count > settings.MaximumSelectedUnits)
            throw new BadRequestException("The item selection is too large.");
    }

    private async Task<AccountPaymentActor> ResolveActorAsync(
        TableServiceSession session, string? participantCredential, bool guest, AccountPaymentActor? staffActor,
        CancellationToken cancellationToken)
    {
        if (!guest) return staffActor!;
        return await guestAuthorization.AuthorizeLockedAsync(session, participantCredential, cancellationToken);
    }

    private async Task<AccountPaymentOperationDto?> FindExistingQuoteAsync(
        Guid sessionId, Guid operationId, AccountPaymentActor actor, string hash,
        CancellationToken cancellationToken)
    {
        var existing = await context.AccountPaymentAttempts.Include(value => value.Allocations)
            .Include(value => value.CashCollectionReceipt)
            .SingleOrDefaultAsync(value => value.OperationId == operationId, cancellationToken);
        if (existing is not null) return RequireReplay(existing, sessionId, actor, hash);
        if (await context.AccountEqualSharePlans.AnyAsync(value => value.OperationId == operationId, cancellationToken))
            throw new ConflictException("The operation id has already been used.");
        return null;
    }

    private static void RequireSegmentLimit(IReadOnlyList<AccountDebtSegment> segments, int maximumSegments)
    {
        if (segments.Count > maximumSegments)
            throw new BadRequestException("The payment scope exceeds the configured segment limit.");
    }

    private static CashSettlementQuote? ResolveCashSettlement(
        long amount, long tipMinor, string currency, PaymentMethod paymentMethod,
        AccountPaymentSettings settings)
    {
        long tenderExactMinor;
        try
        {
            tenderExactMinor = checked(amount + tipMinor);
        }
        catch (OverflowException)
        {
            throw new BadRequestException("The payment and tip exceed the supported amount.");
        }
        return AccountCashSettlementPolicy.ResolveConfigured(currency, paymentMethod, tenderExactMinor, settings);
    }

    private static void RequireCapacityForQuote(AccountCashRefundHistoryCapacitySize capacity, int segmentCount)
    {
        if (!capacity.CanAdd(AccountCashRefundHistoryCapacityGrowth.ForQuote(segmentCount)))
            throw new ConflictException("The table account exceeds the supported financial history limit.");
    }

    private static AccountPaymentAttempt CreateAttempt(QuoteIdentity identity, QuoteFinancials financials)
    {
        var attempt = new AccountPaymentAttempt
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = identity.SessionId,
            OperationId = identity.Request.OperationId,
            ActorId = identity.Actor.ActorId,
            ActorKind = identity.Actor.Kind,
            Mode = identity.Request.Mode,
            State = AccountPaymentState.Quoted,
            PaymentMethod = identity.Request.PaymentMethod,
            Version = 1,
            ExpectedAccountRevision = identity.Request.ExpectedAccountRevision,
            AmountMinor = financials.AmountMinor,
            TipMinor = financials.TipMinor,
            Currency = financials.Currency,
            PayloadHash = identity.Hash,
            QuoteExpiresAt = financials.QuoteExpiresAt,
            EqualSharePlanId = AttemptSharePlanId(identity.Request),
            EqualShareOrdinal = AttemptShareOrdinal(identity.Request),
            CreatedAt = financials.CreatedAt,
            CreatedBy = identity.Actor.AuditIdentifier
        };
        attempt.Allocations = AccountPaymentSnapshots.Allocations(
            attempt.Id, financials.Segments, identity.Actor.AuditIdentifier);
        attempt.SnapshotJson = AccountPaymentSnapshots.Serialize(CreateQuoteSnapshot(
            identity.Request, financials.AmountMinor, financials.TipMinor, financials.Currency,
            financials.QuoteExpiresAt, financials.Segments, financials.CashSettlement));
        return attempt;
    }

    private static Guid? AttemptSharePlanId(CreateAccountPaymentQuoteRequest request) =>
        request.Mode == AccountPaymentMode.CustomAmount ? request.CustomSharePlanId : request.EqualSharePlanId;

    private static int? AttemptShareOrdinal(CreateAccountPaymentQuoteRequest request) =>
        request.Mode == AccountPaymentMode.CustomAmount ? request.CustomShareOrdinal : request.EqualShareOrdinal;

    private static AccountPaymentQuoteSnapshot CreateQuoteSnapshot(
        CreateAccountPaymentQuoteRequest request, long amount, long tipMinor, string currency,
        DateTime quoteExpires, IReadOnlyList<AccountDebtSegment> segments, CashSettlementQuote? cashSettlement) =>
        new(request.ExpectedAccountRevision, request.Mode, request.PaymentMethod, amount,
            currency, quoteExpires,
            request.Mode == AccountPaymentMode.Equal ? request.EqualSharePlanId : null,
            request.Mode == AccountPaymentMode.Equal ? request.EqualShareOrdinal : null,
            AccountPaymentSnapshots.ToDtos(segments), cashSettlement)
        {
            TipMinor = tipMinor,
            CustomSharePlanId = request.CustomSharePlanId,
            CustomShareOrdinal = request.CustomShareOrdinal
        };

    private async Task<IReadOnlyList<AccountDebtSegment>> SelectSegmentsAsync(
        Guid sessionId, CreateAccountPaymentQuoteRequest request,
        AccountPaymentAccountSnapshot account, CancellationToken cancellationToken)
    {
        var segments = request.Mode switch
        {
            AccountPaymentMode.Items => ReadItemSegments(request, account),
            AccountPaymentMode.Amount => AccountDebtMath.Amount(account.Debt.Available, request.AmountMinor!.Value),
            AccountPaymentMode.Full => account.Debt.Available,
            AccountPaymentMode.Equal or AccountPaymentMode.CustomAmount => await ReadShareSegmentsAsync(
                sessionId, request, cancellationToken),
            _ => throw new BadRequestException("The selected payment mode is not supported.")
        };

        RequirePayableScope(account.Debt.Available, segments);
        return segments;
    }

    private static IReadOnlyList<AccountDebtSegment> ReadItemSegments(
        CreateAccountPaymentQuoteRequest request, AccountPaymentAccountSnapshot account) =>
        AccountDebtMath.Items(account.Debt.Available,
            request.SelectedUnits.Select(value => new AccountUnitIdentity(
                value.OrderId, value.OrderItemId, value.Ordinal)).ToArray());

    private async Task<IReadOnlyList<AccountDebtSegment>> ReadShareSegmentsAsync(
        Guid sessionId, CreateAccountPaymentQuoteRequest request, CancellationToken cancellationToken)
    {
        var isCustom = request.Mode == AccountPaymentMode.CustomAmount;
        var planId = isCustom ? request.CustomSharePlanId : request.EqualSharePlanId;
        var ordinal = isCustom ? request.CustomShareOrdinal : request.EqualShareOrdinal;
        var plan = await context.AccountEqualSharePlans.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == planId && value.ServiceSessionId == sessionId,
            cancellationToken) ?? throw new NotFoundException("The equal-share plan was not found.");
        if (plan.InvalidatedAt is not null)
            throw new ConflictException("The equal-share plan is no longer available to this cashier.");
        if ((plan.CustomAmountsJson is not null) != isCustom)
            throw new ConflictException("The selected guest split plan does not match the payment mode.");
        if (ordinal > plan.ShareCount)
            throw new BadRequestException("The equal-share position is outside the reviewed plan.");
        var scope = AccountPaymentSnapshots.ReadScope(plan.ScopeJson);
        return isCustom
            ? AccountCustomShareScopeMath.ForShare(scope,
                AccountPaymentSnapshots.Deserialize<List<long>>(plan.CustomAmountsJson!), ordinal!.Value)
            : AccountEqualScopeMath.ForShare(
                scope, plan.ShareCount, ordinal!.Value, plan.RoundingIncrementMinor);
    }

    private static void RequirePayableScope(
        IReadOnlyList<AccountDebtSegment> available, IReadOnlyList<AccountDebtSegment> segments)
    {
        // A quote is not a reservation. Keep the exact reviewed scope and fail rather than silently
        // shifting it if another contribution reserved or captured any of these units meanwhile.
        AccountDebtMath.Subtract(available, segments);
        if (segments.Count == 0 || AccountDebtMath.Total(segments) <= 0)
        {
            throw new BadRequestException("The payment scope has no payable value.");
        }
    }

    private static AccountPaymentOperationDto RequireReplay(
        AccountPaymentAttempt attempt, Guid sessionId, AccountPaymentActor actor, string hash)
    {
        if (attempt.ServiceSessionId != sessionId || attempt.ActorId != actor.ActorId
            || attempt.ActorKind != actor.Kind || attempt.PayloadHash != hash)
            throw new ConflictException("The operation id has already been used.");
        return AccountPaymentSnapshots.ToOperation(attempt);
    }

    private static void RequireOpen(TableServiceSession session)
    {
        if (session.Status != TableServiceSessionStatus.Open)
            throw new ConflictException("A closed table visit cannot accept a payment quote.");
        if (string.IsNullOrWhiteSpace(session.Currency))
            throw new ConflictException("The table visit has no declared currency for payment quotes.");
    }

    private sealed record QuoteIdentity(
        Guid SessionId, CreateAccountPaymentQuoteRequest Request, AccountPaymentActor Actor, string Hash);

    private sealed record QuoteFinancials(
        long AmountMinor, long TipMinor, string Currency, DateTime QuoteExpiresAt, DateTime CreatedAt,
        IReadOnlyList<AccountDebtSegment> Segments, CashSettlementQuote? CashSettlement);
}
