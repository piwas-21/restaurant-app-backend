using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

/// <summary>Commits the original request before any provider I/O; replay never reprices or rotates identity.</summary>
public sealed class CustomerOrderCheckoutJournalStore(ApplicationDbContext context,
    ICustomerCheckoutGuard guard, ICustomerOrderOperationStore operations, CustomMediator mediator,
    IStripeGateway gateway, IOptions<StripeSettings> stripe,
    IOptions<CustomerHostedCheckoutSettings> options, ICheckoutChargeResolver charges,
    IOnlinePaymentIntentGuard intents, TimeProvider clock) : ICustomerOrderCheckoutJournalStore
{
    public async Task<CustomerOrderCheckoutJournal> FreezeAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken)
    {
        if (proof.SchemaVersion != 1) throw new BadRequestException("Unsupported native payment schema.");
        var owner = guard.EnsureOwner(sessionId);
        var operation = await operations.RecoverAsync(sessionId, proof.OperationId, proof.PayloadHash, cancellationToken);
        if (operation.Outcome != CustomerOrderOperationOutcome.Applied || operation.OrderId is not { } orderId)
            throw Unavailable();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Erasure locks related orders before the user. Match that order to avoid a cycle.
            await using var orderLock = await OrderAccountMutationScope.BeginAsync(context, orderId, cancellationToken);
            if (owner is { } userId)
                await CustomerAccountWriteGuard.AcquireAsync(context, userId, true, cancellationToken);
            var order = await context.Orders.AsNoTracking().SingleOrDefaultAsync(value => value.Id == orderId
                && value.UserId == owner && !value.IsDeleted, cancellationToken) ?? throw Unavailable();
            if (order.ServiceSessionId is not null) throw Unavailable();
            var existing = await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.OrderId == orderId, cancellationToken);
            var now = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds()).UtcDateTime;
            if (existing is not null)
            {
                if (existing.OperationId != proof.OperationId) throw Unavailable();
                CustomerOrderCheckoutReplay.Read(existing, now, forCreate: false);
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }
            var settings = options.Value;
            if (!settings.Enabled || !settings.IsValid()) throw Unavailable();
            var bootstrap = await CustomerBasketAvailability.ReadOrderingAsync(mediator, cancellationToken);
            // The public policy deliberately leaves provider readiness unknown. This private
            // preparation proves configured provider context separately; it does not activate the app.
            if (bootstrap.Capabilities.GetValueOrDefault("online-payments") is not ("unknown" or "enabled"))
                throw Unavailable();
            OnlinePaymentEligibility.EnsurePayable(order);
            await intents.EnsureProcessingAsync(orderId, cancellationToken);
            if (await context.OrderCheckoutSessions.AnyAsync(value => value.OrderId == orderId, cancellationToken))
                throw new ConflictException("Reconcile the existing checkout before starting a native payment.");
            var billing = await context.OrderBillingSnapshots.AsNoTracking()
                .SingleOrDefaultAsync(value => value.OrderId == orderId, cancellationToken) ?? throw Unavailable();
            var (amount, fee) = charges.Resolve(order.PayableTotal, billing.Currency);
            if (amount.Minor != billing.TotalMinor)
                throw new ConflictException("The accepted order changed. Ask the restaurant to review its payment.");
            var routing = NativeCheckoutRouting.Create(settings.LinkOrigin, settings.TenantSlug, orderId,
                proof.OperationId, StripeProviderContext.Read(gateway, stripe.Value));
            var request = new CheckoutSessionRequest
            {
                OrderId = orderId,
                OrderNumber = order.OrderNumber,
                Currency = amount.Currency,
                AmountMinor = amount.Minor,
                ApplicationFeeMinor = fee,
                ExpiresAt = now.Add(CustomerOrderCheckoutReplay.SessionLifetime),
                IdempotencyKey = CustomerOrderCheckoutReplay.Key(proof.OperationId),
                NativeRouting = routing
            };
            var journal = new CustomerOrderCheckoutJournal
            {
                OrderId = request.OrderId,
                OperationId = proof.OperationId,
                OrderNumber = request.OrderNumber,
                Currency = request.Currency,
                AmountMinor = request.AmountMinor,
                ApplicationFeeMinor = request.ApplicationFeeMinor,
                ProviderAccountId = routing.Context.ConnectedAccountId,
                ProviderLiveMode = routing.Context.LiveMode,
                ReturnUrl = routing.ReturnUrl,
                CreateIdempotencyKey = request.IdempotencyKey,
                CreatePayloadHash = CustomerOrderCheckoutReplay.Hash(request),
                StartedAt = now,
                ExpiresAt = request.ExpiresAt,
                MaximumCreateRetryAt = now.Add(CustomerOrderCheckoutReplay.MaximumReplayLifetime)
            };
            CustomerOrderCheckoutReplay.Read(journal, now, forCreate: true);
            context.Set<CustomerOrderCheckoutJournal>().Add(journal);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return journal;
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private static NotFoundException Unavailable() => new("Native payment operation unavailable.");
}
