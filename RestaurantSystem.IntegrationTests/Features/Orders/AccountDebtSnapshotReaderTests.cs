using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class AccountDebtSnapshotReaderTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(AccountPaymentState.Reserved)]
    [InlineData(AccountPaymentState.Starting)]
    [InlineData(AccountPaymentState.Processing)]
    [InlineData(AccountPaymentState.CancelRequested)]
    [InlineData(AccountPaymentState.ReconciliationRequired)]
    [InlineData((AccountPaymentState)999)]
    public async Task ExpiredReservationDoesNotEraseAnUnresolvedContribution(AccountPaymentState state)
    {
        var sessionId = await SeedLedger(state);
        await using var context = DatabaseFixture.CreateContext();
        var snapshot = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, CancellationToken.None);
        snapshot.Session.Id.Should().Be(sessionId);
        snapshot.Money.Currency.Should().Be("CHF");
        snapshot.Debt.OutstandingMinor.Should().Be(8000);
        snapshot.Debt.ReservedMinor.Should().Be(600);
        snapshot.Debt.AvailableMinor.Should().Be(7400);
        snapshot.Debt.Available.Select(segment => (segment.StartOrdinal, segment.Count, segment.MinorPerUnit))
            .Should().Equal((1, 1, 3000L), (2, 1, 4400L));
    }

    [Theory]
    [InlineData(AccountPaymentState.Quoted)]
    [InlineData(AccountPaymentState.Released)]
    [InlineData(AccountPaymentState.Failed)]
    public async Task NonholdingAttemptDoesNotClaimTheBill(AccountPaymentState state)
    {
        var sessionId = await SeedLedger(state);
        await using var context = DatabaseFixture.CreateContext();
        var snapshot = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, CancellationToken.None);
        snapshot.Debt.OutstandingMinor.Should().Be(8000);
        snapshot.Debt.ReservedMinor.Should().Be(0);
        snapshot.Debt.AvailableMinor.Should().Be(8000);
    }

    [Fact]
    public async Task MissingCapturedTenderLinkFailsClosedInsteadOfReportingPaid()
    {
        var sessionId = await SeedLedger(AccountPaymentState.Released, linkCapturedTender: false);
        await using var context = DatabaseFixture.CreateContext();
        var read = () => new AccountDebtSnapshotReader(context).ReadAsync(sessionId, CancellationToken.None);
        await read.Should().ThrowAsync<ConflictException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Positive_paid_summary_without_captured_rows_fails_closed(bool pendingOnly)
    {
        var sessionId = await SeedUnbackedPaidSummary(pendingOnly);
        await using var context = DatabaseFixture.CreateContext();
        var read = () => new AccountDebtSnapshotReader(context).ReadAsync(sessionId, CancellationToken.None);
        await read.Should().ThrowAsync<ConflictException>().WithMessage("*without captured tender evidence*");
    }

    [Fact]
    public async Task Completed_but_uncleared_checkout_blocks_account_read_and_close_even_when_feature_is_off()
    {
        var sessionId = await SeedStripeCheckout(reconciled: false);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var read = () => new AccountDebtSnapshotReader(context).ReadAsync(sessionId, CancellationToken.None);
            await read.Should().ThrowAsync<ConflictException>().WithMessage("*Stripe checkout*verified capture evidence*");
        }

        await using var closeContext = DatabaseFixture.CreateContext();
        await using var transaction = await closeContext.Database.BeginTransactionAsync();
        var close = () => AccountPaymentCloseGuard.RequireClosableAsync(
            closeContext, sessionId, features: null, cancellationToken: CancellationToken.None);
        await close.Should().ThrowAsync<ConflictException>().WithMessage("*Stripe checkout*verified capture evidence*");
    }

    [Fact]
    public async Task Active_checkout_blocks_account_collection_even_when_order_summary_is_unpaid()
    {
        var sessionId = await SeedStripeCheckout(reconciled: false, status: CheckoutSessionStatus.Created);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var read = () => new AccountDebtSnapshotReader(context).ReadAsync(sessionId, CancellationToken.None);
            await read.Should().ThrowAsync<ConflictException>().WithMessage("*checkout is still active*");
        }

        await using var closeContext = DatabaseFixture.CreateContext();
        await using var transaction = await closeContext.Database.BeginTransactionAsync();
        var close = () => AccountPaymentCloseGuard.RequireClosableAsync(
            closeContext, sessionId, features: null, cancellationToken: CancellationToken.None);
        await close.Should().ThrowAsync<ConflictException>().WithMessage("*checkout is still active*");
    }

    [Fact]
    public async Task Succeeded_checkout_with_exact_tender_evidence_can_reduce_account_debt_and_close()
    {
        var sessionId = await SeedStripeCheckout(reconciled: true);
        await using (var context = DatabaseFixture.CreateContext())
            (await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, CancellationToken.None))
                .Debt.OutstandingMinor.Should().Be(0);

        await using var closeContext = DatabaseFixture.CreateContext();
        await using var transaction = await closeContext.Database.BeginTransactionAsync();
        var close = () => AccountPaymentCloseGuard.RequireClosableAsync(
            closeContext, sessionId, features: null, cancellationToken: CancellationToken.None);
        await close.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Verified_online_account_allocation_without_checkout_can_resolve_the_account()
    {
        var sessionId = await SeedStripeCheckout(reconciled: true, corruption: "verified-account-allocation");
        await using (var context = DatabaseFixture.CreateContext())
            (await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, CancellationToken.None))
                .Debt.OutstandingMinor.Should().Be(0);

        await using var closeContext = DatabaseFixture.CreateContext();
        await using var transaction = await closeContext.Database.BeginTransactionAsync();
        var close = () => AccountPaymentCloseGuard.RequireClosableAsync(
            closeContext, sessionId, features: null, cancellationToken: CancellationToken.None);
        await close.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("pi_shared_test")]
    [InlineData("ch_shared_test")]
    public async Task Verified_online_account_attempt_can_allocate_a_shared_provider_capture_across_orders(
        string providerChargeId)
    {
        var sessionId = await SeedMultiOrderOnlineAccountCapture(providerChargeId);
        await using var context = DatabaseFixture.CreateContext();
        var snapshot = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, CancellationToken.None);
        snapshot.Debt.OutstandingMinor.Should().Be(0);
    }

    [Theory]
    [InlineData("last-error")]
    [InlineData("missing-tender-link")]
    [InlineData("payment-intent")]
    [InlineData("tender-payment-intent")]
    [InlineData("tender-amount")]
    [InlineData("tender-currency")]
    [InlineData("checkout-amount")]
    [InlineData("connected-account")]
    [InlineData("missing-payment-intent")]
    [InlineData("wrong-tender-link")]
    [InlineData("unproven-account-allocation")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("session-id")]
    [InlineData("unknown-status")]
    [InlineData("missing-checkout")]
    public async Task Reconciled_online_tender_without_exact_checkout_proof_fails_closed(string corruption)
    {
        var sessionId = await SeedStripeCheckout(reconciled: true, corruption: corruption);
        await using (var evidence = DatabaseFixture.CreateContext())
        {
            var order = await evidence.Orders.Include(value => value.Payments)
                .SingleAsync(value => value.ServiceSessionId == sessionId);
            var checkouts = await evidence.OrderCheckoutSessions
                .Where(value => value.OrderId == order.Id).ToListAsync();
            var attempts = await evidence.AccountPaymentAttempts
                .Where(value => value.ServiceSessionId == sessionId)
                .Include(value => value.Allocations).ThenInclude(value => value.OrderPayment)
                .ToListAsync();
            var validate = () => AccountCheckoutEvidenceGuard.Validate(
                [order], checkouts, attempts, new AccountMoney("CHF"));
            validate.Should().Throw<ConflictException>();
        }

        await using var context = DatabaseFixture.CreateContext();
        var read = () => new AccountDebtSnapshotReader(context).ReadAsync(sessionId, CancellationToken.None);
        await read.Should().ThrowAsync<ConflictException>();

        await using var closeContext = DatabaseFixture.CreateContext();
        await using var transaction = await closeContext.Database.BeginTransactionAsync();
        var close = () => AccountPaymentCloseGuard.RequireClosableAsync(
            closeContext, sessionId, features: null, cancellationToken: CancellationToken.None);
        await close.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task CurrencyMismatchInReservedScopeRequiresReconciliation()
    {
        var sessionId = await SeedLedger(AccountPaymentState.Reserved, reservationCurrency: "EUR");
        await using var context = DatabaseFixture.CreateContext();
        var read = () => new AccountDebtSnapshotReader(context).ReadAsync(sessionId, CancellationToken.None);
        await read.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Resolved_replacement_removes_only_the_old_unit_and_counts_the_supplement_once()
    {
        var sessionId = await SeedResolvedReplacement();

        await using var context = DatabaseFixture.CreateContext();
        var snapshot = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, CancellationToken.None);

        snapshot.Debt.OutstandingMinor.Should().Be(3200);
        snapshot.Debt.Available.Should().ContainSingle(value => value.TotalMinor == 1200
            && value.OrderId != Guid.Empty, "replacement additions use their linked ordinary order exactly once");
    }

    [Fact]
    public async Task Pending_amendment_credit_blocks_account_collection_projection()
    {
        var sessionId = await SeedLedger(AccountPaymentState.Released);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var source = await context.Orders.SingleAsync(value => value.ServiceSessionId == sessionId);
            var financial = new OrderAmendmentFinancialPreviewDto("CHF", 0, 1000, -1000, 1000,
                OrderAmendmentFinancialResolutionStatus.Pending,
                OrderAmendmentCreditState.PendingAllocationReview,
                OrderAmendmentLoyaltyState.None, OrderAmendmentRefundState.PendingTillRefund);
            context.Set<OrderAmendment>().Add(NewAmendment(sessionId, source.Id, "[]", financial));
            await context.SaveChangesAsync();
        }

        await using var verify = DatabaseFixture.CreateContext();
        var read = () => new AccountDebtSnapshotReader(verify).ReadAsync(sessionId, CancellationToken.None);
        await read.Should().ThrowAsync<ConflictException>().WithMessage("*unresolved*");
    }

    [Fact]
    public async Task Pending_amendment_blocks_close_even_when_account_payments_are_disabled()
    {
        var sessionId = await SeedPendingAmendmentWithoutPaymentLedger();

        await using var context = DatabaseFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var close = () => AccountPaymentCloseGuard.RequireClosableAsync(
            context, sessionId, features: null, cancellationToken: CancellationToken.None);

        await close.Should().ThrowAsync<ConflictException>().WithMessage("*unresolved*");
    }

    [Fact]
    public async Task Amendment_guard_refuses_captured_and_reserved_units_but_allows_an_unrelated_line()
    {
        var sessionId = await SeedLedger(AccountPaymentState.Reserved);
        Guid orderId;
        Guid allocatedItemId;
        Guid unrelatedItemId;
        await using (var context = DatabaseFixture.CreateContext())
        {
            var order = await context.Orders.SingleAsync(value => value.ServiceSessionId == sessionId);
            orderId = order.Id;
            allocatedItemId = await context.OrderItems.Where(value => value.OrderId == order.Id)
                .Select(value => value.Id).SingleAsync();
            var unrelated = new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductName = "Unclaimed line",
                Quantity = 1,
                ItemTotal = 0m,
                CreatedBy = "test"
            };
            context.OrderItems.Add(unrelated);
            unrelatedItemId = unrelated.Id;
            await context.SaveChangesAsync();
        }

        await using var verify = DatabaseFixture.CreateContext();
        var guard = new AccountPaymentOrderAmendmentReservationGuard(verify);
        var captured = () => guard.AssertUnitsMutableAsync(orderId,
            [new(allocatedItemId, 1, 1, WholeLine: false)], CancellationToken.None);
        var reserved = () => guard.AssertUnitsMutableAsync(orderId,
            [new(allocatedItemId, 2, 1, WholeLine: false)], CancellationToken.None);
        var unrelatedCheck = () => guard.AssertUnitsMutableAsync(orderId,
            [new(unrelatedItemId, 1, 1, WholeLine: false)], CancellationToken.None);

        await captured.Should().ThrowAsync<ConflictException>();
        await reserved.Should().ThrowAsync<ConflictException>();
        await unrelatedCheck.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Unknown_future_payment_state_keeps_its_allocation_amendment_protected()
    {
        var sessionId = await SeedLedger(AccountPaymentState.Released);
        Guid orderId;
        Guid itemId;
        await using (var update = DatabaseFixture.CreateContext())
        {
            var order = await update.Orders.SingleAsync(value => value.ServiceSessionId == sessionId);
            orderId = order.Id;
            itemId = await update.OrderItems.Where(value => value.OrderId == order.Id)
                .Select(value => value.Id).SingleAsync();
            var attempt = await update.AccountPaymentAttempts.SingleAsync(value =>
                value.ServiceSessionId == sessionId && value.State == AccountPaymentState.Released);
            attempt.State = (AccountPaymentState)999;
            await update.SaveChangesAsync();
        }

        await using var verify = DatabaseFixture.CreateContext();
        var guard = new AccountPaymentOrderAmendmentReservationGuard(verify);
        var check = () => guard.AssertUnitsMutableAsync(orderId,
            [new(itemId, 2, 1, WholeLine: false)], CancellationToken.None);
        await check.Should().ThrowAsync<ConflictException>();
    }

    private async Task<Guid> SeedUnbackedPaidSummary(bool pendingOnly)
    {
        await using var context = DatabaseFixture.CreateContext();
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 999,
            Currency = "CHF",
            OpenedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        var order = NewOrder(session.Id, 100m);
        order.TotalPaid = 20m;
        order.RemainingAmount = 80m;
        order.PaymentStatus = PaymentStatus.PartiallyPaid;
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductName = "Frozen dish",
            Quantity = 1,
            ItemTotal = 100m,
            CreatedBy = "test"
        });
        if (pendingOnly)
        {
            order.Payments.Add(new OrderPayment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Amount = 20m,
                PaymentMethod = PaymentMethod.Cash,
                Status = PaymentStatus.Pending,
                Currency = "CHF",
                PaymentDate = DateTime.UtcNow,
                CreatedBy = "test"
            });
        }
        context.TableServiceSessions.Add(session);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return session.Id;
    }

    private async Task<Guid> SeedStripeCheckout(
        bool reconciled, CheckoutSessionStatus status = CheckoutSessionStatus.Completed, string? corruption = null)
    {
        await using var context = DatabaseFixture.CreateContext();
        var now = DateTime.UtcNow;
        var serviceSession = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 998,
            Currency = "CHF",
            OpenedAt = now,
            CreatedBy = "test"
        };
        var order = NewOrder(serviceSession.Id, 100m);
        order.TotalPaid = status == CheckoutSessionStatus.Created ? 0m : 100m;
        order.RemainingAmount = status == CheckoutSessionStatus.Created ? 100m : 0m;
        order.PaymentStatus = status == CheckoutSessionStatus.Created
            ? PaymentStatus.Pending : PaymentStatus.Completed;
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductName = "Online checkout dish",
            Quantity = 1,
            ItemTotal = 100m,
            CreatedBy = "test"
        });
        var payment = new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Amount = 100m,
            PaymentMethod = PaymentMethod.OnlinePayment,
            Status = status == CheckoutSessionStatus.Created ? PaymentStatus.Processing : PaymentStatus.Completed,
            Currency = "CHF",
            PaymentGateway = "Stripe",
            TransactionId = "pi_account_test",
            PaymentDate = now,
            CreatedBy = "test"
        };
        order.Payments.Add(payment);
        var checkout = new OrderCheckoutSession
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            SessionId = "cs_account_test",
            PaymentIntentId = "pi_account_test",
            Status = status,
            Currency = "chf",
            AmountMinor = 10000,
            AmountReceivedMinor = 10000,
            IdempotencyKey = $"checkout:{order.Id}:1",
            ExpiresAt = now.AddMinutes(31),
            ConnectedAccountId = "acct_account_test",
            OrderPaymentId = payment.Id,
            ReconciledAt = reconciled ? now : null,
            CreatedAt = now,
            CreatedBy = "test"
        };
        var accountAttempts = new List<AccountPaymentAttempt>();
        switch (corruption)
        {
            case "last-error":
                checkout.LastError = "Payment intent requires manual reconciliation.";
                break;
            case "missing-tender-link":
                checkout.OrderPaymentId = null;
                break;
            case "payment-intent":
                checkout.PaymentIntentId = "pi_other";
                break;
            case "tender-payment-intent":
                payment.TransactionId = "pi_other";
                break;
            case "tender-amount":
                payment.Amount = 99m;
                order.TotalPaid = 99m;
                order.RemainingAmount = 1m;
                order.PaymentStatus = PaymentStatus.PartiallyPaid;
                break;
            case "tender-currency":
                payment.Currency = "EUR";
                break;
            case "checkout-amount":
                checkout.AmountMinor = 9900;
                break;
            case "connected-account":
                checkout.ConnectedAccountId = "account_test";
                break;
            case "missing-payment-intent":
                checkout.PaymentIntentId = null;
                break;
            case "wrong-tender-link":
                {
                    var unlinkedTender = new OrderPayment
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        Amount = 100m,
                        PaymentMethod = PaymentMethod.OnlinePayment,
                        Status = PaymentStatus.Failed,
                        Currency = "CHF",
                        PaymentGateway = "Stripe",
                        TransactionId = "pi_account_test",
                        PaymentDate = now,
                        CreatedBy = "test"
                    };
                    order.Payments.Add(unlinkedTender);
                    checkout.OrderPaymentId = unlinkedTender.Id;
                    break;
                }
            case "unproven-account-allocation":
            case "verified-account-allocation":
                {
                    var attempt = Attempt(serviceSession.Id, AccountPaymentState.Captured, 10000, "CHF");
                    attempt.PaymentMethod = PaymentMethod.OnlinePayment;
                    attempt.ProviderChargeId = corruption == "verified-account-allocation"
                        ? payment.TransactionId : null;
                    attempt.ProviderAccountId = corruption == "verified-account-allocation"
                        ? "acct_account_test" : null;
                    attempt.Allocations.Add(Allocation(attempt.Id, order.Id, order.Items.Single().Id,
                        1, 10000, payment.Id));
                    accountAttempts.Add(attempt);
                    break;
                }
            case "amount":
                checkout.AmountReceivedMinor = 9900;
                break;
            case "currency":
                checkout.Currency = "EUR";
                break;
            case "session-id":
                checkout.SessionId = "checkout_test";
                break;
            case "unknown-status":
                checkout.Status = (CheckoutSessionStatus)999;
                break;
            case "missing-checkout":
                break;
        }

        context.TableServiceSessions.Add(serviceSession);
        context.Orders.Add(order);
        context.AccountPaymentAttempts.AddRange(accountAttempts);
        if (corruption is not ("missing-checkout" or "unproven-account-allocation" or "verified-account-allocation"))
            context.OrderCheckoutSessions.Add(checkout);
        await context.SaveChangesAsync();
        return serviceSession.Id;
    }

    private async Task<Guid> SeedMultiOrderOnlineAccountCapture(string providerChargeId)
    {
        await using var context = DatabaseFixture.CreateContext();
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 997,
            Currency = "CHF",
            OpenedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        var orders = new List<Order>();
        var attempt = Attempt(session.Id, AccountPaymentState.Captured, 10000, "CHF");
        attempt.PaymentMethod = PaymentMethod.OnlinePayment;
        attempt.ProviderChargeId = providerChargeId;
        attempt.ProviderAccountId = "acct_shared_test";

        for (var index = 0; index < 2; index++)
        {
            var order = NewOrder(session.Id, 50m);
            order.TotalPaid = 50m;
            order.RemainingAmount = 0m;
            order.PaymentStatus = PaymentStatus.Completed;
            var item = new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductName = $"Shared dish {index}",
                Quantity = 1,
                ItemTotal = 50m,
                CreatedBy = "test"
            };
            var payment = new OrderPayment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Amount = 50m,
                PaymentMethod = PaymentMethod.OnlinePayment,
                Status = PaymentStatus.Completed,
                Currency = "CHF",
                PaymentGateway = "Stripe",
                TransactionId = providerChargeId,
                PaymentDate = DateTime.UtcNow,
                CreatedBy = "test"
            };
            order.Items.Add(item);
            order.Payments.Add(payment);
            attempt.Allocations.Add(Allocation(attempt.Id, order.Id, item.Id, 1, 5000, payment.Id));
            orders.Add(order);
        }

        context.TableServiceSessions.Add(session);
        context.Orders.AddRange(orders);
        context.AccountPaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return session.Id;
    }

    private async Task<Guid> SeedLedger(AccountPaymentState state,
        bool linkCapturedTender = true, string reservationCurrency = "CHF")
    {
        await using var context = DatabaseFixture.CreateContext();
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 999,
            Currency = "CHF",
            OpenedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ACCOUNT-TEST",
            ServiceSessionId = session.Id,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            Total = 100m,
            TotalPaid = 20m,
            RemainingAmount = 80m,
            PaymentStatus = PaymentStatus.PartiallyPaid,
            OrderDate = DateTime.UtcNow,
            CreatedBy = "test"
        };
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductName = "Frozen dish",
            Quantity = 2,
            ItemTotal = 100m,
            CreatedBy = "test"
        };
        var payment = new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Amount = 20m,
            PaymentMethod = PaymentMethod.Cash,
            Status = PaymentStatus.Completed,
            Currency = "CHF",
            PaymentDate = DateTime.UtcNow,
            CreatedBy = "test"
        };
        var captured = Attempt(session.Id, AccountPaymentState.Captured, 2000, "CHF");
        var reserved = Attempt(session.Id, state, 600, reservationCurrency);
        captured.Allocations.Add(Allocation(captured.Id, order.Id, item.Id, 1, 2000,
            linkCapturedTender ? payment.Id : null));
        reserved.ReservationExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        reserved.Allocations.Add(Allocation(reserved.Id, order.Id, item.Id, 2, 600));
        context.TableServiceSessions.Add(session);
        context.Orders.Add(order);
        context.OrderItems.Add(item);
        context.OrderPayments.Add(payment);
        context.AccountPaymentAttempts.AddRange(captured, reserved);
        await context.SaveChangesAsync();
        return session.Id;
    }

    private async Task<Guid> SeedResolvedReplacement()
    {
        await using var context = DatabaseFixture.CreateContext();
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 999,
            Currency = "CHF",
            OpenedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        var source = NewOrder(session.Id, 30.01m);
        var oldItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = source.Id,
            ProductName = "Original dish",
            Quantity = 3,
            UnitPrice = 10.0033m,
            ItemTotal = 30.01m,
            CreatedBy = "test"
        };
        source.Items.Add(oldItem);
        var supplement = NewOrder(session.Id, 12m);
        var newItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = supplement.Id,
            ProductName = "Replacement dish",
            Quantity = 1,
            UnitPrice = 12m,
            ItemTotal = 12m,
            CreatedBy = "test"
        };
        supplement.Items.Add(newItem);
        var change = new OrderAmendmentChangeSnapshot(oldItem.Id, OrderAmendmentChangeKind.Replace,
            1, 1, false, new OrderItemDto { Id = oldItem.Id, Quantity = 1 },
            new OrderItemDto { Id = newItem.Id, Quantity = 1 }, supplement.Id);
        var financial = new OrderAmendmentFinancialPreviewDto("CHF", 1200, 1001, 199, 1001,
            OrderAmendmentFinancialResolutionStatus.Resolved, OrderAmendmentCreditState.BalanceReduction,
            OrderAmendmentLoyaltyState.None, OrderAmendmentRefundState.None);
        context.TableServiceSessions.Add(session);
        context.Orders.AddRange(source, supplement);
        context.Set<OrderAmendment>().Add(NewAmendment(session.Id, source.Id,
            OrderAmendmentJson.Serialize(new[] { change }), financial, supplement.Id));
        await context.SaveChangesAsync();
        return session.Id;
    }

    private async Task<Guid> SeedPendingAmendmentWithoutPaymentLedger()
    {
        await using var context = DatabaseFixture.CreateContext();
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 999,
            Currency = "CHF",
            OpenedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        var source = NewOrder(session.Id, 10m);
        var pending = new OrderAmendmentFinancialPreviewDto("CHF", 0, 500, -500, 500,
            OrderAmendmentFinancialResolutionStatus.Pending,
            OrderAmendmentCreditState.PendingAllocationReview,
            OrderAmendmentLoyaltyState.None, OrderAmendmentRefundState.PendingTillRefund);
        context.TableServiceSessions.Add(session);
        context.Orders.Add(source);
        context.Set<OrderAmendment>().Add(NewAmendment(session.Id, source.Id, "[]", pending));
        await context.SaveChangesAsync();
        return session.Id;
    }

    private static OrderAmendment NewAmendment(Guid sessionId, Guid sourceOrderId,
        string changesJson, OrderAmendmentFinancialPreviewDto financial, Guid? supplementId = null) => new()
        {
            Id = Guid.NewGuid(),
            SourceOrderId = sourceOrderId,
            ServiceSessionId = sessionId,
            SupplementOrderId = supplementId,
            ClientOperationId = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
            ActorRole = UserRole.Server.ToString(),
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('a', 64),
            CommitPayloadHash = new string('b', 64),
            ExpectedOrderVersion = 1,
            CommittedAccountRevision = 2,
            ExpiresAt = DateTime.UtcNow,
            CommittedAt = DateTime.UtcNow,
            RequestJson = "{}",
            ChangesJson = changesJson,
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = OrderAmendmentJson.Serialize(financial),
            CreatedBy = "test"
        };

    private static Order NewOrder(Guid sessionId, decimal total) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = $"P6-{Guid.NewGuid():N}"[..15],
        ServiceSessionId = sessionId,
        Type = OrderType.DineIn,
        Status = OrderStatus.Completed,
        PaymentStatus = PaymentStatus.Pending,
        SubTotal = total,
        Total = total,
        RemainingAmount = total,
        OrderDate = DateTime.UtcNow,
        CreatedBy = "test"
    };

    private static AccountPaymentAttempt Attempt(Guid sessionId, AccountPaymentState state,
        long amountMinor, string currency) => new()
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = sessionId,
            OperationId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            ActorKind = AccountPaymentActorKind.Staff,
            Mode = AccountPaymentMode.Amount,
            State = state,
            PaymentMethod = PaymentMethod.Cash,
            ExpectedAccountRevision = 1,
            AmountMinor = amountMinor,
            Currency = currency,
            PayloadHash = new string('a', 64),
            SnapshotJson = "{}",
            QuoteExpiresAt = DateTime.UtcNow.AddMinutes(1),
            CreatedBy = "test"
        };

    private static AccountPaymentAllocation Allocation(Guid attemptId, Guid orderId, Guid itemId,
        int ordinal, long amountMinor, Guid? paymentId = null) => new()
        {
            Id = Guid.NewGuid(),
            AttemptId = attemptId,
            OrderId = orderId,
            OrderItemId = itemId,
            OrderPaymentId = paymentId,
            StartOrdinal = ordinal,
            UnitCount = 1,
            MinorPerUnit = amountMinor,
            AmountMinor = amountMinor,
            CreatedBy = "test"
        };
}
