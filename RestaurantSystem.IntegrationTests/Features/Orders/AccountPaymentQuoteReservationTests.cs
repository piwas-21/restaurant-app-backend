using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class AccountPaymentQuoteReservationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly Guid _actorId = Guid.NewGuid();

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Quote_replay_precedes_stale_revision_and_changed_payload_is_refused()
    {
        var account = await SeedAccount(12m, quantity: 2);
        var operationId = Guid.NewGuid();
        var request = ItemsQuote(operationId, account, revision: 1, ordinal: 1);
        var quoted = await CreateQuote(account.SessionId, request);

        await using (var context = fixture.CreateContext())
        {
            var session = await context.TableServiceSessions.SingleAsync(value => value.Id == account.SessionId);
            session.RecordAccountChange();
            await context.SaveChangesAsync();
        }

        var replay = await CreateQuote(account.SessionId, request);
        replay.Should().BeEquivalentTo(quoted);
        replay.Allocations.Sum(value => value.AmountMinor).Should().Be(600);

        var changedPayload = ItemsQuote(operationId, account, revision: 1, ordinal: 2);
        var conflict = () => CreateQuote(account.SessionId, changedPayload);
        await conflict.Should().ThrowAsync<ConflictException>();

        var staleReservation = () => Reserve(account.SessionId, operationId, expectedVersion: 1, revision: 1);
        await staleReservation.Should().ThrowAsync<ConflictException>();

        var staleNewOperation = ItemsQuote(Guid.NewGuid(), account, revision: 1, ordinal: 2);
        var stale = () => CreateQuote(account.SessionId, staleNewOperation);
        await stale.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Quote_replay_treats_an_omitted_tip_as_an_explicit_zero()
    {
        var account = await SeedAccount(10m, quantity: 2);
        var operationId = Guid.NewGuid();
        var legacyRequest = ItemsQuote(operationId, account, revision: 1, ordinal: 1);

        var first = await CreateQuote(account.SessionId, legacyRequest);
        var replay = await CreateQuote(account.SessionId, legacyRequest with { TipMinor = 0 });

        replay.Should().BeEquivalentTo(first);
        replay.TipMinor.Should().Be(0);
        await using var verify = fixture.CreateContext();
        (await verify.AccountPaymentAttempts.CountAsync(value => value.OperationId == operationId)).Should().Be(1);
        (await verify.AccountPaymentAttempts.SingleAsync(value => value.OperationId == operationId))
            .TipMinor.Should().Be(0);
    }

    [Theory]
    [InlineData(PaymentMethod.DebitCard)]
    [InlineData(PaymentMethod.OnlinePayment)]
    [InlineData(PaymentMethod.MobilePayment)]
    [InlineData(PaymentMethod.BankTransfer)]
    public async Task Quote_rejects_methods_without_a_collection_path_before_persisting(
        PaymentMethod unsupportedMethod)
    {
        var account = await SeedAccount(10m);
        var request = ItemsQuote(Guid.NewGuid(), account, revision: 1, ordinal: 1) with
        {
            PaymentMethod = unsupportedMethod
        };

        var quote = () => CreateQuote(account.SessionId, request);
        await quote.Should().ThrowAsync<BadRequestException>()
            .WithMessage("*supports cash or manual card only*");

        await using var verify = fixture.CreateContext();
        (await verify.AccountPaymentAttempts.AnyAsync(value => value.OperationId == request.OperationId))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Session_lock_serializes_overlapping_reservations_and_retry_returns_original_result()
    {
        var account = await SeedAccount(20m, quantity: 2);
        var first = await CreateQuote(account.SessionId,
            ItemsQuote(Guid.NewGuid(), account, revision: 1, ordinal: 1));
        var second = await CreateQuote(account.SessionId,
            ItemsQuote(Guid.NewGuid(), account, revision: 1, ordinal: 1));

        var outcomes = await Task.WhenAll(
            Reserve(account.SessionId, first.OperationId, expectedVersion: 1, revision: 1)
                .ContinueAsConflict(),
            Reserve(account.SessionId, second.OperationId, expectedVersion: 1, revision: 1)
                .ContinueAsConflict());

        outcomes.Count(value => value).Should().Be(1);
        await using var winnerContext = fixture.CreateContext();
        var winner = await winnerContext.AccountPaymentAttempts.SingleAsync(value =>
            value.ServiceSessionId == account.SessionId && value.State == AccountPaymentState.Reserved);
        var replay = await Reserve(account.SessionId, winner.OperationId, expectedVersion: 1, revision: 1);
        replay.State.Should().Be(AccountPaymentState.Reserved);
        replay.Version.Should().Be(2);

        await using var verify = fixture.CreateContext();
        var attempts = await verify.AccountPaymentAttempts.Where(value => value.ServiceSessionId == account.SessionId)
            .ToListAsync();
        attempts.Count(value => value.State == AccountPaymentState.Reserved).Should().Be(1);
        attempts.Count(value => value.State == AccountPaymentState.Quoted).Should().Be(1);
        var snapshot = await new AccountDebtSnapshotReader(verify).ReadAsync(account.SessionId, CancellationToken.None);
        snapshot.Debt.ReservedMinor.Should().Be(1000);
        snapshot.Debt.AvailableMinor.Should().Be(1000);
    }

    [Fact]
    public async Task Equal_plan_is_frozen_to_its_reviewed_round_and_shares_can_reserve_out_of_order()
    {
        var account = await SeedAccount(10m);
        var planOperation = Guid.NewGuid();
        var plan = await CreatePlan(account.SessionId, new CreateAccountEqualSharePlanRequest
        {
            OperationId = planOperation,
            ExpectedAccountRevision = 1,
            ShareCount = 3
        });
        plan.TotalMinor.Should().Be(1000);
        plan.Scope.Should().ContainSingle().Which.OrderId.Should().Be(account.OrderId);

        var laterRound = await SeedOrder(account.SessionId, 5m);
        await using (var context = fixture.CreateContext())
        {
            var session = await context.TableServiceSessions.SingleAsync(value => value.Id == account.SessionId);
            session.RecordAccountChange();
            await context.SaveChangesAsync();
        }

        var last = await CreateQuote(account.SessionId, EqualQuote(Guid.NewGuid(), plan.PlanId, 3, revision: 2));
        var first = await CreateQuote(account.SessionId, EqualQuote(Guid.NewGuid(), plan.PlanId, 1, revision: 2));
        last.AmountMinor.Should().Be(340);
        first.AmountMinor.Should().Be(330);
        last.Allocations.Should().OnlyContain(value => value.OrderId == account.OrderId);
        first.Allocations.Should().OnlyContain(value => value.OrderId == account.OrderId);

        (await Reserve(account.SessionId, first.OperationId, expectedVersion: 1, revision: 2))
            .State.Should().Be(AccountPaymentState.Reserved);
        (await Reserve(account.SessionId, last.OperationId, expectedVersion: 1, revision: 2))
            .State.Should().Be(AccountPaymentState.Reserved);

        await using var verify = fixture.CreateContext();
        var snapshot = await new AccountDebtSnapshotReader(verify).ReadAsync(account.SessionId, CancellationToken.None);
        snapshot.Debt.ReservedMinor.Should().Be(670);
        snapshot.Debt.AvailableMinor.Should().Be(830);
        snapshot.Debt.Available.Should().Contain(value => value.OrderId == laterRound.OrderId);
        snapshot.Debt.Available.Should().Contain(value => value.OrderId == account.OrderId);
    }

    [Fact]
    public async Task Custom_guest_amounts_are_frozen_and_tip_is_separate_from_reserved_food_debt()
    {
        var account = await SeedAccount(10m);
        var plan = await CreatePlan(account.SessionId,
            NewPlanRequest(Guid.NewGuid(), 1, 3) with { CustomAmountsMinor = [250, 325, 425] });

        plan.CustomAmountsMinor.Should().BeEquivalentTo([250L, 325L, 425L]);
        var quote = await CreateQuote(account.SessionId, new CreateAccountPaymentQuoteRequest
        {
            OperationId = Guid.NewGuid(),
            ExpectedAccountRevision = 1,
            Mode = AccountPaymentMode.CustomAmount,
            PaymentMethod = PaymentMethod.Cash,
            CustomSharePlanId = plan.PlanId,
            CustomShareOrdinal = 2,
            TipMinor = 125,
        });

        quote.Mode.Should().Be(AccountPaymentMode.CustomAmount);
        quote.CustomSharePlanId.Should().Be(plan.PlanId);
        quote.CustomShareOrdinal.Should().Be(2);
        quote.AmountMinor.Should().Be(325);
        quote.TipMinor.Should().Be(125);
        quote.CashSettlement!.DueAmountMinor.Should().Be(450,
            "the cash due is food allocation plus tip while the account allocation remains food-only");
        quote.Allocations.Sum(value => value.AmountMinor).Should().Be(325);

        (await Reserve(account.SessionId, quote.OperationId, expectedVersion: 1, revision: 1))
            .State.Should().Be(AccountPaymentState.Reserved);
        await using var verify = fixture.CreateContext();
        var debt = await new AccountDebtSnapshotReader(verify).ReadAsync(account.SessionId, CancellationToken.None);
        debt.Debt.ReservedMinor.Should().Be(325);
        debt.Debt.AvailableMinor.Should().Be(675);
        (await verify.AccountPaymentAttempts.SingleAsync(value => value.OperationId == quote.OperationId))
            .TipMinor.Should().Be(125);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Custom_guest_shares_capture_disjoint_amounts_in_either_order(bool reverseOrder)
    {
        var account = await SeedAccount(10m);
        var amounts = new long[] { 250, 325, 425 };
        var plan = await CreatePlan(account.SessionId,
            NewPlanRequest(Guid.NewGuid(), 1, amounts.Length) with { CustomAmountsMinor = amounts });
        var ordinals = reverseOrder ? new[] { 3, 2, 1 } : new[] { 1, 2, 3 };
        long revision = 1;

        foreach (var ordinal in ordinals)
        {
            var quote = await CreateQuote(account.SessionId, new CreateAccountPaymentQuoteRequest
            {
                OperationId = Guid.NewGuid(),
                ExpectedAccountRevision = revision,
                Mode = AccountPaymentMode.CustomAmount,
                PaymentMethod = PaymentMethod.CreditCard,
                CustomSharePlanId = plan.PlanId,
                CustomShareOrdinal = ordinal
            });
            quote.AmountMinor.Should().Be(amounts[ordinal - 1]);
            quote.Allocations.Sum(value => value.AmountMinor).Should().Be(amounts[ordinal - 1]);

            await Reserve(account.SessionId, quote.OperationId, expectedVersion: 1, revision: revision);
            var captured = await Capture(account.SessionId, quote.OperationId, expectedVersion: 2,
                receivedMinor: null);
            captured.State.Should().Be(AccountPaymentState.Captured);
            revision++;
        }

        await using var verify = fixture.CreateContext();
        var attempts = await verify.AccountPaymentAttempts
            .Where(value => value.ServiceSessionId == account.SessionId)
            .Include(value => value.Allocations)
            .ToListAsync();
        attempts.Should().HaveCount(3).And.OnlyContain(value => value.State == AccountPaymentState.Captured);
        attempts.Sum(value => value.Allocations.Sum(allocation => allocation.AmountMinor)).Should().Be(1000);
        attempts.Select(value => value.AmountMinor).Should().BeEquivalentTo(amounts);
        var debt = await new AccountDebtSnapshotReader(verify).ReadAsync(account.SessionId, CancellationToken.None);
        debt.Debt.OutstandingMinor.Should().Be(0);
        debt.Debt.ReservedMinor.Should().Be(0);
        debt.Debt.AvailableMinor.Should().Be(0);
    }

    [Fact]
    public async Task Cashiers_can_claim_shared_equal_plan_slots_without_sharing_operation_lookup()
    {
        var account = await SeedAccount(10m);
        var plan = await CreatePlan(account.SessionId, NewPlanRequest(Guid.NewGuid(), 1, 2));
        var firstActor = Guid.NewGuid();
        var secondActor = Guid.NewGuid();
        var firstOperation = Guid.NewGuid();
        var secondOperation = Guid.NewGuid();
        var firstQuote = await CreateQuote(account.SessionId,
            EqualQuote(firstOperation, plan.PlanId, 1, revision: 1), firstActor);
        var secondQuote = await CreateQuote(account.SessionId,
            EqualQuote(secondOperation, plan.PlanId, 1, revision: 1), secondActor);
        firstQuote.AmountMinor.Should().Be(500);
        secondQuote.AmountMinor.Should().Be(500);

        var claims = await Task.WhenAll(
            Reserve(account.SessionId, firstOperation, expectedVersion: 1, revision: 1, firstActor).ContinueAsConflict(),
            Reserve(account.SessionId, secondOperation, expectedVersion: 1, revision: 1, secondActor).ContinueAsConflict());

        claims.Count(value => value).Should().Be(1);
        var winner = claims[0] ? (firstActor, firstOperation) : (secondActor, secondOperation);
        var loser = claims[0] ? (secondActor, secondOperation) : (firstActor, firstOperation);
        (await GetOperation(account.SessionId, winner.Item2, winner.Item1)).State
            .Should().Be(AccountPaymentState.Reserved);
        var crossActorLookup = () => GetOperation(account.SessionId, winner.Item2, loser.Item1);
        await crossActorLookup.Should().ThrowAsync<RestaurantSystem.Api.Common.Exceptions.NotFoundException>();

        var nextOperation = Guid.NewGuid();
        var nextSlot = await CreateQuote(account.SessionId,
            EqualQuote(nextOperation, plan.PlanId, 2, revision: 1), loser.Item1);
        nextSlot.EqualShareOrdinal.Should().Be(2);
        (await Reserve(account.SessionId, nextOperation, expectedVersion: 1, revision: 1, loser.Item1))
            .State.Should().Be(AccountPaymentState.Reserved);

        await using var verify = fixture.CreateContext();
        var attempts = await verify.AccountPaymentAttempts
            .Where(value => value.ServiceSessionId == account.SessionId)
            .ToListAsync();
        attempts.Count(value => value.State == AccountPaymentState.Reserved).Should().Be(2);
        attempts.Where(value => value.EqualShareOrdinal == 1)
            .Should().ContainSingle(value => value.OperationId == winner.Item2);
        attempts.Where(value => value.EqualShareOrdinal == 2)
            .Should().ContainSingle(value => value.OperationId == nextOperation);
        attempts.Count(value => value.EqualShareOrdinal == 1 && value.State == AccountPaymentState.Quoted)
            .Should().Be(1, "the losing attempt remains owned by its actor and does not claim the shared slot");
    }

    [Fact]
    public async Task Active_equal_plan_requires_explicit_supersession()
    {
        var account = await SeedAccount(10m);
        var current = await CreatePlan(account.SessionId, NewPlanRequest(Guid.NewGuid(), 1, 2));

        var implicitPlan = () => CreatePlan(account.SessionId, NewPlanRequest(Guid.NewGuid(), 1, 2));
        await implicitPlan.Should().ThrowAsync<ConflictException>();

        var replacement = await CreatePlan(account.SessionId,
            NewPlanRequest(Guid.NewGuid(), 1, 2) with { SupersedesPlanId = current.PlanId });
        replacement.TotalMinor.Should().Be(current.TotalMinor);
        await using var verify = fixture.CreateContext();
        (await verify.AccountEqualSharePlans.SingleAsync(value => value.Id == current.PlanId))
            .InvalidatedAt.Should().NotBeNull();
        (await verify.AccountEqualSharePlans.SingleAsync(value => value.Id == replacement.PlanId))
            .SupersedesPlanId.Should().Be(current.PlanId);
    }

    [Fact]
    public async Task Released_slot_can_be_re_reviewed_in_an_explicitly_superseding_plan()
    {
        var account = await SeedAccount(10m);
        var oldPlan = await CreatePlan(account.SessionId, NewPlanRequest(Guid.NewGuid(), 1, 3));
        var oldSlot = await CreateQuote(account.SessionId,
            EqualQuote(Guid.NewGuid(), oldPlan.PlanId, 1, revision: 1));
        await Reserve(account.SessionId, oldSlot.OperationId, expectedVersion: 1, revision: 1);
        var released = await Release(account.SessionId, oldSlot.OperationId, expectedVersion: 2);
        released.State.Should().Be(AccountPaymentState.Released);

        var newPlan = await CreatePlan(account.SessionId,
            NewPlanRequest(Guid.NewGuid(), 1, 3) with { SupersedesPlanId = oldPlan.PlanId });
        newPlan.Scope.Should().BeEquivalentTo(oldPlan.Scope);
        var newSlot = await CreateQuote(account.SessionId,
            EqualQuote(Guid.NewGuid(), newPlan.PlanId, 1, revision: 1));
        (await Reserve(account.SessionId, newSlot.OperationId, expectedVersion: 1, revision: 1))
            .State.Should().Be(AccountPaymentState.Reserved);
    }

    [Theory]
    [InlineData(AccountPaymentState.Reserved)]
    [InlineData(AccountPaymentState.Starting)]
    [InlineData(AccountPaymentState.Processing)]
    [InlineData(AccountPaymentState.Captured)]
    [InlineData(AccountPaymentState.CancelRequested)]
    [InlineData(AccountPaymentState.ReconciliationRequired)]
    [InlineData((AccountPaymentState)999)]
    public async Task Plan_with_reserved_captured_or_provider_pending_slot_cannot_be_superseded(
        AccountPaymentState blockingState)
    {
        var account = await SeedAccount(10m);
        var plan = await CreatePlan(account.SessionId, NewPlanRequest(Guid.NewGuid(), 1, 3));
        var slot = await CreateQuote(account.SessionId,
            EqualQuote(Guid.NewGuid(), plan.PlanId, 1, revision: 1));
        await Reserve(account.SessionId, slot.OperationId, expectedVersion: 1, revision: 1);
        if (blockingState != AccountPaymentState.Reserved)
        {
            await using var update = fixture.CreateContext();
            var attempt = await update.AccountPaymentAttempts.SingleAsync(value => value.OperationId == slot.OperationId);
            attempt.State = blockingState;
            await update.SaveChangesAsync();
        }

        var supersede = () => CreatePlan(account.SessionId,
            NewPlanRequest(Guid.NewGuid(), 1, 3) with { SupersedesPlanId = plan.PlanId });
        await supersede.Should().ThrowAsync<ConflictException>()
            .WithMessage("*captured or provider-pending*");
        await using var verify = fixture.CreateContext();
        (await verify.AccountEqualSharePlans.SingleAsync(value => value.Id == plan.PlanId))
            .InvalidatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Equal_share_slot_has_one_reserved_or_captured_operation_until_released()
    {
        var account = await SeedAccount(10m);
        var plan = await CreatePlan(account.SessionId, NewPlanRequest(Guid.NewGuid(), 1, 3));
        var first = await CreateQuote(account.SessionId, EqualQuote(Guid.NewGuid(), plan.PlanId, 1, revision: 1));
        var competing = await CreateQuote(account.SessionId, EqualQuote(Guid.NewGuid(), plan.PlanId, 1, revision: 1));

        await Reserve(account.SessionId, first.OperationId, expectedVersion: 1, revision: 1);
        var competingReserve = () => Reserve(account.SessionId, competing.OperationId, expectedVersion: 1, revision: 1);
        await competingReserve.Should().ThrowAsync<ConflictException>().WithMessage("*already has a reserved or captured*");

        await Release(account.SessionId, first.OperationId, expectedVersion: 2);
        (await Reserve(account.SessionId, competing.OperationId, expectedVersion: 1, revision: 1))
            .State.Should().Be(AccountPaymentState.Reserved);
    }

    [Fact]
    public async Task Captured_equal_share_slot_cannot_be_reserved_again_from_a_fresh_quote()
    {
        var account = await SeedAccount(10m);
        var plan = await CreatePlan(account.SessionId, NewPlanRequest(Guid.NewGuid(), 1, 3));
        var first = await CreateQuote(account.SessionId, EqualQuote(Guid.NewGuid(), plan.PlanId, 1, revision: 1));
        await Reserve(account.SessionId, first.OperationId, expectedVersion: 1, revision: 1);
        await Capture(account.SessionId, first.OperationId, expectedVersion: 2);

        var reReviewed = await CreateQuote(account.SessionId,
            EqualQuote(Guid.NewGuid(), plan.PlanId, 1, revision: 2));
        var reserve = () => Reserve(account.SessionId, reReviewed.OperationId, expectedVersion: 1, revision: 2);
        await reserve.Should().ThrowAsync<ConflictException>().WithMessage("*already has a reserved or captured*");
    }

    [Fact]
    public async Task Local_release_and_lookup_survive_flag_off_but_started_state_cannot_be_released()
    {
        var account = await SeedAccount(10m);
        var quote = await CreateQuote(account.SessionId,
            ItemsQuote(Guid.NewGuid(), account, revision: 1, ordinal: 1));
        await Reserve(account.SessionId, quote.OperationId, expectedVersion: 1, revision: 1);

        var disabled = new TenantFeatures(Options.Create(new TenantFeatureSettings()));
        var released = await Release(account.SessionId, quote.OperationId, expectedVersion: 2, disabled);
        released.State.Should().Be(AccountPaymentState.Released);
        (await GetOperation(account.SessionId, quote.OperationId)).State.Should().Be(AccountPaymentState.Released);

        var startedId = Guid.NewGuid();
        var startedQuote = await CreateQuote(account.SessionId,
            ItemsQuote(startedId, account, revision: 1, ordinal: 1));
        await using (var context = fixture.CreateContext())
        {
            var attempt = await context.AccountPaymentAttempts.SingleAsync(value => value.OperationId == startedId);
            attempt.State = AccountPaymentState.Processing;
            attempt.Version = 3;
            attempt.ReservedAt = DateTime.UtcNow.AddMinutes(-1);
            attempt.StartedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        var cannotRelease = () => Release(account.SessionId, startedId, expectedVersion: 3, disabled);
        await cannotRelease.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task One_operation_key_cannot_create_a_plan_and_quote_on_different_visits_concurrently()
    {
        var firstAccount = await SeedAccount(10m);
        var secondAccount = await SeedAccount(10m);
        var operationId = Guid.NewGuid();
        var quote = CreateQuote(firstAccount.SessionId,
            ItemsQuote(operationId, firstAccount, revision: 1, ordinal: 1)).ContinueAsConflict();
        var plan = CreatePlan(secondAccount.SessionId, new CreateAccountEqualSharePlanRequest
        {
            OperationId = operationId,
            ExpectedAccountRevision = 1,
            ShareCount = 2
        }).ContinueAsConflict();

        var outcomes = await Task.WhenAll(quote, plan);
        outcomes.Count(value => value).Should().Be(1);
    }

    private async Task<AccountPaymentOperationDto> CreateQuote(
        Guid sessionId, CreateAccountPaymentQuoteRequest request, Guid? actorId = null)
    {
        await using var context = fixture.CreateContext();
        return await QuoteService(context, actorId).CreateQuoteAsync(sessionId, request, CancellationToken.None);
    }

    private async Task<AccountEqualSharePlanDto> CreatePlan(
        Guid sessionId, CreateAccountEqualSharePlanRequest request)
    {
        await using var context = fixture.CreateContext();
        var features = Features();
        return await new AccountEqualSharePlanService(context, ActorResolver(), GuestAuthorization(), GuestPolicy(features),
            features, Options.Create(new AccountPaymentSettings()), TimeProvider.System)
            .CreateAsync(sessionId, request, CancellationToken.None);
    }

    private async Task<AccountPaymentOperationDto> Reserve(
        Guid sessionId, Guid operationId, int expectedVersion, long revision, Guid? actorId = null)
    {
        await using var context = fixture.CreateContext();
        return await ReservationService(context, actorId).ReserveAsync(sessionId, operationId,
            new ReserveAccountPaymentRequest { ExpectedVersion = expectedVersion, ExpectedAccountRevision = revision },
            CancellationToken.None);
    }

    private async Task<AccountPaymentOperationDto> Release(
        Guid sessionId, Guid operationId, int expectedVersion, ITenantFeatures? features = null)
    {
        await using var context = fixture.CreateContext();
        var activeFeatures = features ?? Features();
        return await new AccountPaymentReservationService(context, ActorResolver(), GuestAuthorization(),
            GuestPolicy(activeFeatures), activeFeatures, Options.Create(new AccountPaymentSettings()), TimeProvider.System)
            .ReleaseAsync(sessionId, operationId, new ReleaseAccountPaymentRequest { ExpectedVersion = expectedVersion },
                CancellationToken.None);
    }

    private async Task<AccountPaymentOperationDto> Capture(
        Guid sessionId, Guid operationId, int expectedVersion, long? receivedMinor = 335)
    {
        await using var context = fixture.CreateContext();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(value => value.GetAuditIdentifier()).Returns(_actorId.ToString());
        var fidelity = new Mock<IOrderFidelityCoordinator>();
        var service = new AccountPaymentCaptureService(context, ActorResolver(),
            new AccountPaymentCaptureWriter(context, currentUser.Object, TimeProvider.System),
            fidelity.Object, TimeProvider.System, NullLogger<AccountPaymentCaptureService>.Instance);
        return await service.CaptureManualAsync(sessionId, operationId,
            new CaptureAccountPaymentRequest { ExpectedVersion = expectedVersion, ReceivedMinor = receivedMinor }, CancellationToken.None);
    }

    private static CreateAccountEqualSharePlanRequest NewPlanRequest(
        Guid operationId, long revision, int shareCount) => new()
        {
            OperationId = operationId,
            ExpectedAccountRevision = revision,
            ShareCount = shareCount
        };

    private async Task<AccountPaymentOperationDto> GetOperation(
        Guid sessionId, Guid operationId, Guid? actorId = null)
    {
        await using var context = fixture.CreateContext();
        return await new AccountPaymentOperationReader(context, ActorResolver(actorId), GuestAuthorization())
            .GetAttemptAsync(sessionId, operationId, CancellationToken.None);
    }

    private AccountPaymentQuoteService QuoteService(
        RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext context, Guid? actorId = null) =>
        new(context, ActorResolver(actorId), GuestAuthorization(), GuestPolicy(Features()), Features(),
            Options.Create(new AccountPaymentSettings()), TimeProvider.System);

    private AccountPaymentReservationService ReservationService(
        RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext context, Guid? actorId = null) =>
        new(context, ActorResolver(actorId), GuestAuthorization(), GuestPolicy(Features()), Features(),
            Options.Create(new AccountPaymentSettings()), TimeProvider.System);

    private static ITableGuestParticipantPaymentAuthorization GuestAuthorization() =>
        Mock.Of<ITableGuestParticipantPaymentAuthorization>();

    private static GuestAccountPaymentPolicy GuestPolicy(ITenantFeatures features) => new(
        features,
        Mock.Of<ITenantModules>(value => value.IsEnabled(ModuleIds.OnlinePayments)),
        Mock.Of<IStripeGateway>(value => value.IsConfigured),
        Options.Create(new AccountOnlineContributionSettings { SettlementCurrency = "CHF" }));

    private TestActorResolver ActorResolver(Guid? actorId = null) => new(actorId ?? _actorId);

    private static TenantFeatures Features() =>
        new(Options.Create(new TenantFeatureSettings { TableAccountPaymentsV1 = true }));

    private async Task<(Guid SessionId, Guid OrderId, Guid ItemId)> SeedAccount(decimal total, int quantity = 1)
    {
        await using var context = fixture.CreateContext();
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            Currency = "CHF",
            OpenedAt = DateTime.UtcNow,
            CreatedBy = "p6-test"
        };
        var order = CreateOrder(session.Id, total);
        var item = CreateItem(order.Id, total, quantity);
        order.Items.Add(item);
        context.TableServiceSessions.Add(session);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return (session.Id, order.Id, item.Id);
    }

    private async Task<(Guid OrderId, Guid ItemId)> SeedOrder(Guid sessionId, decimal total)
    {
        await using var context = fixture.CreateContext();
        var order = CreateOrder(sessionId, total);
        var item = CreateItem(order.Id, total, 1);
        order.Items.Add(item);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return (order.Id, item.Id);
    }

    private static Order CreateOrder(Guid sessionId, decimal total) => new()
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
        CreatedBy = "p6-test"
    };

    private static OrderItem CreateItem(Guid orderId, decimal total, int quantity) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        ProductName = "Frozen test dish",
        Quantity = quantity,
        UnitPrice = total / quantity,
        ItemTotal = total,
        CreatedBy = "p6-test"
    };

    private static CreateAccountPaymentQuoteRequest ItemsQuote(
        Guid operationId, (Guid SessionId, Guid OrderId, Guid ItemId) account, long revision, int ordinal) => new()
        {
            OperationId = operationId,
            ExpectedAccountRevision = revision,
            Mode = AccountPaymentMode.Items,
            PaymentMethod = PaymentMethod.Cash,
            SelectedUnits = [new AccountPaymentUnitSelection(account.OrderId, account.ItemId, ordinal)]
        };

    private static CreateAccountPaymentQuoteRequest EqualQuote(
        Guid operationId, Guid planId, int ordinal, long revision) => new()
        {
            OperationId = operationId,
            ExpectedAccountRevision = revision,
            Mode = AccountPaymentMode.Equal,
            PaymentMethod = PaymentMethod.Cash,
            EqualSharePlanId = planId,
            EqualShareOrdinal = ordinal
        };

    private sealed class TestActorResolver(Guid actorId) : IAccountPaymentActorResolver
    {
        public bool CanStartCollection => true;
        public void RequireNewCollection()
        {
            // These payment lifecycle fixtures grant staff authority; policy is covered separately.
        }
        public AccountPaymentActor ResolveStaffActor() =>
            new(actorId, AccountPaymentActorKind.Staff, actorId.ToString(), UserRole.Cashier);
    }
}

internal static class AccountPaymentTestTasks
{
    internal static async Task<bool> ContinueAsConflict<T>(this Task<T> task)
    {
        try { await task; return true; }
        catch (ConflictException) { return false; }
    }
}
