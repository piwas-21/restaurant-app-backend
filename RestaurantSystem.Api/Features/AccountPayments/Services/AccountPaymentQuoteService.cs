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
        if (sessionId == Guid.Empty) throw new BadRequestException("A table visit is required.");
        AccountPaymentRequestRules.ValidateQuote(request, allowOnlinePayment: guest);
        if (guest && request.PaymentMethod != PaymentMethod.OnlinePayment)
            throw new BadRequestException("Guest account payments support online payment only.");
        if (request.TipMinor > 0 && (guest || request.PaymentMethod == PaymentMethod.OnlinePayment))
            throw new BadRequestException("Tips are available only for staff-collected cash or card payments.");
        var settings = options.Value;
        if (request.SelectedUnits.Count > settings.MaximumSelectedUnits)
            throw new BadRequestException("The item selection is too large.");

        var staffActor = guest ? null : actors.ResolveStaffActor();
        var hash = AccountPaymentRequestRules.QuoteHash(sessionId, request, allowOnlinePayment: guest);
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        try
        {
            var session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken)
                ?? throw new NotFoundException("Table account was not found.");
            var actor = guest
                ? await guestAuthorization.AuthorizeLockedAsync(session, participantCredential, cancellationToken)
                : staffActor!;
            await AccountPaymentOperationKeyLock.AcquireAsync(context, request.OperationId, cancellationToken);
            var existing = await context.AccountPaymentAttempts.Include(value => value.Allocations)
                .Include(value => value.CashCollectionReceipt)
                .SingleOrDefaultAsync(value => value.OperationId == request.OperationId, cancellationToken);
            if (existing is not null)
            {
                var replay = RequireReplay(existing, sessionId, actor, hash);
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
            if (await context.AccountEqualSharePlans.AnyAsync(
                    value => value.OperationId == request.OperationId, cancellationToken))
                throw new ConflictException("The operation id has already been used.");

            RequireOpen(session);
            AccountPaymentRequestRules.RequireCurrentRevision(session.AccountRevision, request.ExpectedAccountRevision);
            var account = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, cancellationToken);
            var segments = await SelectSegmentsAsync(sessionId, request, account, cancellationToken);
            if (segments.Count > settings.MaximumScopeSegments)
                throw new BadRequestException("The payment scope exceeds the configured segment limit.");
            var amount = AccountDebtMath.Total(segments);
            if (guest) guestPolicy.RequireContribution(amount, account.Money.Currency);
            long tenderExactMinor;
            try
            {
                tenderExactMinor = checked(amount + request.TipMinor);
            }
            catch (OverflowException)
            {
                throw new BadRequestException("The payment and tip exceed the supported amount.");
            }
            var cashSettlement = AccountCashSettlementPolicy.ResolveConfigured(
                account.Money.Currency, request.PaymentMethod, tenderExactMinor, settings);
            var capacity = await AccountCashRefundHistoryCapacityReader.ReadQuoteSizeAsync(
                context, sessionId, cancellationToken);
            if (!capacity.CanAdd(AccountCashRefundHistoryCapacityGrowth.ForQuote(segments.Count)))
                throw new ConflictException("The table account exceeds the supported financial history limit.");
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var quoteExpires = now.AddMinutes(settings.QuoteLifetimeMinutes);
            var attempt = new AccountPaymentAttempt
            {
                Id = Guid.NewGuid(),
                ServiceSessionId = sessionId,
                OperationId = request.OperationId,
                ActorId = actor.ActorId,
                ActorKind = actor.Kind,
                Mode = request.Mode,
                State = AccountPaymentState.Quoted,
                PaymentMethod = request.PaymentMethod,
                Version = 1,
                ExpectedAccountRevision = request.ExpectedAccountRevision,
                AmountMinor = amount,
                TipMinor = request.TipMinor,
                Currency = account.Money.Currency,
                PayloadHash = hash,
                QuoteExpiresAt = quoteExpires,
                EqualSharePlanId = request.Mode == AccountPaymentMode.CustomAmount
                    ? request.CustomSharePlanId : request.EqualSharePlanId,
                EqualShareOrdinal = request.Mode == AccountPaymentMode.CustomAmount
                    ? request.CustomShareOrdinal : request.EqualShareOrdinal,
                CreatedAt = now,
                CreatedBy = actor.AuditIdentifier
            };
            attempt.Allocations = AccountPaymentSnapshots.Allocations(attempt.Id, segments, actor.AuditIdentifier);
            attempt.SnapshotJson = AccountPaymentSnapshots.Serialize(new AccountPaymentQuoteSnapshot(
                request.ExpectedAccountRevision, request.Mode, request.PaymentMethod, amount,
                account.Money.Currency, quoteExpires,
                request.Mode == AccountPaymentMode.Equal ? request.EqualSharePlanId : null,
                request.Mode == AccountPaymentMode.Equal ? request.EqualShareOrdinal : null,
                AccountPaymentSnapshots.ToDtos(segments), cashSettlement)
            {
                TipMinor = request.TipMinor,
                CustomSharePlanId = request.CustomSharePlanId,
                CustomShareOrdinal = request.CustomShareOrdinal
            });
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

    private async Task<IReadOnlyList<AccountDebtSegment>> SelectSegmentsAsync(
        Guid sessionId, CreateAccountPaymentQuoteRequest request,
        AccountPaymentAccountSnapshot account, CancellationToken cancellationToken)
    {
        IReadOnlyList<AccountDebtSegment> segments;
        if (request.Mode == AccountPaymentMode.Items)
        {
            segments = AccountDebtMath.Items(account.Debt.Available,
                request.SelectedUnits.Select(value => new AccountUnitIdentity(
                    value.OrderId, value.OrderItemId, value.Ordinal)).ToArray());
        }
        else if (request.Mode == AccountPaymentMode.Amount)
        {
            segments = AccountDebtMath.Amount(account.Debt.Available, request.AmountMinor!.Value);
        }
        else if (request.Mode == AccountPaymentMode.Full)
        {
            segments = account.Debt.Available;
        }
        else if (request.Mode is AccountPaymentMode.Equal or AccountPaymentMode.CustomAmount)
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
            segments = isCustom
                ? AccountCustomShareScopeMath.ForShare(scope,
                    AccountPaymentSnapshots.Deserialize<List<long>>(plan.CustomAmountsJson!), ordinal!.Value)
                : AccountEqualScopeMath.ForShare(scope, plan.ShareCount, ordinal!.Value);
        }
        else
        {
            throw new BadRequestException("The selected payment mode is not supported.");
        }

        // A quote is not a reservation. Keep the exact reviewed scope and fail rather than silently
        // shifting it if another contribution reserved or captured any of these units meanwhile.
        AccountDebtMath.Subtract(account.Debt.Available, segments);
        if (segments.Count == 0 || AccountDebtMath.Total(segments) <= 0)
            throw new BadRequestException("The payment scope has no payable value.");
        return segments;
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
}
