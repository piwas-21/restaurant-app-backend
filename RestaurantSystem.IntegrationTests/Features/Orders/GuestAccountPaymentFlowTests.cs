using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class GuestAccountPaymentFlowTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Guest_quotes_are_online_only_idempotent_and_actor_scoped()
    {
        var visit = await SeedAccountAsync();
        var request = ItemsQuote(Guid.NewGuid(), visit, ordinal: 1);
        var quote = await CreateGuestQuoteAsync(visit, visit.FirstToken, request);
        var replay = await CreateGuestQuoteAsync(visit, visit.FirstToken, request);

        replay.Should().BeEquivalentTo(quote);
        quote.PaymentMethod.Should().Be(PaymentMethod.OnlinePayment);
        quote.AmountMinor.Should().Be(1000);

        var otherActorReplay = () => CreateGuestQuoteAsync(visit, visit.SecondToken, request);
        await otherActorReplay.Should().ThrowAsync<ConflictException>();

        var cashRequest = ItemsQuote(Guid.NewGuid(), visit, ordinal: 2) with
        {
            PaymentMethod = PaymentMethod.Cash
        };
        var cashQuote = () => CreateGuestQuoteAsync(visit, visit.FirstToken, cashRequest);
        await cashQuote.Should().ThrowAsync<BadRequestException>()
            .WithMessage("*online payment only*");

        await using var verify = fixture.CreateContext();
        var attempt = await verify.AccountPaymentAttempts.SingleAsync(value => value.OperationId == quote.OperationId);
        attempt.ActorId.Should().Be(visit.FirstParticipantId);
        attempt.ActorKind.Should().Be(AccountPaymentActorKind.GuestParticipant);
        (await verify.AccountPaymentAttempts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Below_provider_minimum_is_rejected_before_an_attempt_or_checkout_journal_exists()
    {
        var visit = await SeedAccountAsync();
        var request = new CreateAccountPaymentQuoteRequest
        {
            OperationId = Guid.NewGuid(),
            ExpectedAccountRevision = 1,
            Mode = AccountPaymentMode.Amount,
            PaymentMethod = PaymentMethod.OnlinePayment,
            AmountMinor = 49
        };
        var quote = () => CreateGuestQuoteAsync(visit, visit.FirstToken, request);
        await quote.Should().ThrowAsync<BadRequestException>();
        await using var readback = fixture.CreateContext();
        (await readback.AccountPaymentAttempts.CountAsync()).Should().Be(0);
        (await readback.AccountCheckoutJournals.CountAsync()).Should().Be(0);
        (await readback.OrderPayments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Valid_participants_share_plan_slots_but_only_one_can_reserve_a_slot()
    {
        var visit = await SeedAccountAsync();
        var plan = await CreateGuestPlanAsync(visit, visit.FirstToken, new CreateAccountEqualSharePlanRequest
        {
            OperationId = Guid.NewGuid(),
            ExpectedAccountRevision = 1,
            ShareCount = 2
        });
        var firstRequest = EqualQuote(Guid.NewGuid(), plan.PlanId, ordinal: 1);
        var secondRequest = firstRequest with { OperationId = Guid.NewGuid() };
        var firstQuote = await CreateGuestQuoteAsync(visit, visit.FirstToken, firstRequest);
        var secondQuote = await CreateGuestQuoteAsync(visit, visit.SecondToken, secondRequest);

        var outcomes = await Task.WhenAll(
            TryReserveGuestAsync(visit, visit.FirstToken, firstQuote.OperationId),
            TryReserveGuestAsync(visit, visit.SecondToken, secondQuote.OperationId));
        outcomes.Count(value => value.Success).Should().Be(1);
        var winner = outcomes.Single(value => value.Success);
        var loser = outcomes.Single(value => !value.Success);

        await using var verify = fixture.CreateContext();
        var attempts = await verify.AccountPaymentAttempts
            .Where(value => value.ServiceSessionId == visit.SessionId)
            .ToListAsync();
        attempts.Count(value => value.State == AccountPaymentState.Reserved).Should().Be(1);
        attempts.Count(value => value.State == AccountPaymentState.Quoted).Should().Be(1);

        await using var firstReadContext = fixture.CreateContext();
        var firstView = await AccountReader(firstReadContext, Features()).GetGuestAsync(
            visit.SessionId, visit.FirstToken, CancellationToken.None);
        firstView.ActiveEqualSharePlan!.IsOwnPlan.Should().BeTrue();
        var firstSummary = firstView.ActiveAttempts.Single(value => value.State == AccountPaymentState.Reserved);
        firstSummary.IsOwnOperation.Should().Be(winner.ActorId == visit.FirstParticipantId);
        firstSummary.OperationId.Should().Be(winner.ActorId == visit.FirstParticipantId
            ? winner.OperationId
            : null);

        await using var secondReadContext = fixture.CreateContext();
        var secondView = await AccountReader(secondReadContext, Features()).GetGuestAsync(
            visit.SessionId, visit.SecondToken, CancellationToken.None);
        secondView.ActiveEqualSharePlan!.IsOwnPlan.Should().BeFalse();
        var secondSummary = secondView.ActiveAttempts.Single(value => value.State == AccountPaymentState.Reserved);
        secondSummary.IsOwnOperation.Should().Be(winner.ActorId == visit.SecondParticipantId);
        secondSummary.OperationId.Should().Be(winner.ActorId == visit.SecondParticipantId
            ? winner.OperationId
            : null);

        await using var winnerContext = fixture.CreateContext();
        var reader = OperationReader(winnerContext);
        var ownOperation = await reader.GetGuestAttemptAsync(
            visit.SessionId, winner.OperationId, winner.Token!, CancellationToken.None);
        ownOperation.State.Should().Be(AccountPaymentState.Reserved);
        var otherActorLookup = () => reader.GetGuestAttemptAsync(
            visit.SessionId, winner.OperationId, loser.Token!, CancellationToken.None);
        await otherActorLookup.Should().ThrowAsync<NotFoundException>();

        await using var planContext = fixture.CreateContext();
        var planReader = OperationReader(planContext);
        (await planReader.GetGuestEqualSharePlanAsync(
            visit.SessionId, plan.OperationId, visit.FirstToken, CancellationToken.None)).PlanId
            .Should().Be(plan.PlanId);
        var otherParticipantPlanLookup = () => planReader.GetGuestEqualSharePlanAsync(
            visit.SessionId, plan.OperationId, visit.SecondToken, CancellationToken.None);
        await otherParticipantPlanLookup.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Guest_and_cashier_cannot_reserve_the_same_shared_equal_share_slot()
    {
        var visit = await SeedAccountAsync();
        var plan = await CreateGuestPlanAsync(visit, visit.FirstToken, new CreateAccountEqualSharePlanRequest
        {
            OperationId = Guid.NewGuid(),
            ExpectedAccountRevision = 1,
            ShareCount = 2
        });
        var guestQuote = await CreateGuestQuoteAsync(
            visit, visit.FirstToken, EqualQuote(Guid.NewGuid(), plan.PlanId, ordinal: 1));
        var staffQuote = await CreateStaffQuoteAsync(
            visit, EqualQuote(Guid.NewGuid(), plan.PlanId, ordinal: 1) with { PaymentMethod = PaymentMethod.Cash });

        var outcomes = await Task.WhenAll(
            TryReserveGuestAsync(visit, visit.FirstToken, guestQuote.OperationId),
            TryReserveStaffAsync(visit, staffQuote.OperationId));

        outcomes.Count(value => value.Success).Should().Be(1);
        await using var verify = fixture.CreateContext();
        var attempts = await verify.AccountPaymentAttempts
            .Where(value => value.ServiceSessionId == visit.SessionId)
            .ToListAsync();
        attempts.Count(value => value.State == AccountPaymentState.Reserved).Should().Be(1);
        attempts.Count(value => value.State == AccountPaymentState.Quoted).Should().Be(1);
        var winner = outcomes.Single(value => value.Success);
        attempts.Single(value => value.State == AccountPaymentState.Reserved)
            .ActorKind.Should().Be(winner.ActorKind);
    }

    [Fact]
    public async Task Legacy_equal_plan_without_typed_owner_is_not_claimed_from_its_audit_identifier()
    {
        var visit = await SeedAccountAsync();
        var legacyOperationId = Guid.NewGuid();
        var legacyPlanId = Guid.NewGuid();
        await using (var seed = fixture.CreateContext())
        {
            seed.AccountEqualSharePlans.Add(new AccountEqualSharePlan
            {
                Id = legacyPlanId,
                ServiceSessionId = visit.SessionId,
                OperationId = legacyOperationId,
                AccountRevision = 1,
                TotalMinor = 2000,
                ShareCount = 2,
                Currency = "CHF",
                PayloadHash = new string('A', 64),
                ScopeJson = AccountPaymentSnapshots.Serialize(new[]
                {
                    new AccountDebtSegment(visit.OrderId, visit.ItemId, 1, 2, 1000)
                }),
                CreatedAt = FixedNow.UtcDateTime,
                CreatedBy = $"GuestParticipant:{visit.FirstParticipantId:N}"
            });
            await seed.SaveChangesAsync();
        }

        await using (var read = fixture.CreateContext())
        {
            var account = await AccountReader(read, Features()).GetGuestAsync(
                visit.SessionId, visit.FirstToken, CancellationToken.None);
            account.ActiveEqualSharePlan!.IsOwnPlan.Should().BeFalse();
            var lookup = () => OperationReader(read).GetGuestEqualSharePlanAsync(
                visit.SessionId, legacyOperationId, visit.FirstToken, CancellationToken.None);
            await lookup.Should().ThrowAsync<NotFoundException>();
        }

        var replay = () => CreateGuestPlanAsync(visit, visit.FirstToken, new CreateAccountEqualSharePlanRequest
        {
            OperationId = legacyOperationId,
            ExpectedAccountRevision = 1,
            ShareCount = 2
        });
        await replay.Should().ThrowAsync<ConflictException>();
        var supersede = () => CreateGuestPlanAsync(visit, visit.FirstToken, new CreateAccountEqualSharePlanRequest
        {
            OperationId = Guid.NewGuid(),
            ExpectedAccountRevision = 1,
            ShareCount = 2,
            SupersedesPlanId = legacyPlanId
        });
        await supersede.Should().ThrowAsync<ConflictException>()
            .WithMessage("*cannot be superseded by this account participant*");
    }

    [Fact]
    public async Task Existing_guest_operation_recovers_after_flags_off_and_started_provider_work_cannot_be_released()
    {
        var visit = await SeedAccountAsync();
        var quote = await CreateGuestQuoteAsync(
            visit, visit.FirstToken, ItemsQuote(Guid.NewGuid(), visit, ordinal: 1));
        var nextQuote = await CreateGuestQuoteAsync(
            visit, visit.FirstToken, ItemsQuote(Guid.NewGuid(), visit, ordinal: 2));
        var reserved = await ReserveGuestAsync(visit, visit.FirstToken, quote.OperationId);
        reserved.State.Should().Be(AccountPaymentState.Reserved);

        var disabledFeatures = Features(guestPayments: false, accountPayments: false, visits: false);
        var disabledReserve = () => ReserveGuestAsync(
            visit, visit.FirstToken, nextQuote.OperationId, disabledFeatures);
        await disabledReserve.Should().ThrowAsync<NotFoundException>();
        var replay = await ReserveGuestAsync(visit, visit.FirstToken, quote.OperationId,
            disabledFeatures, expectedVersion: 1);
        replay.Should().BeEquivalentTo(reserved);
        await using (var readContext = fixture.CreateContext())
        {
            var recovered = await OperationReader(readContext).GetGuestAttemptAsync(
                visit.SessionId, quote.OperationId, visit.FirstToken, CancellationToken.None);
            recovered.Should().BeEquivalentTo(reserved);
            var wrongActor = () => OperationReader(readContext).GetGuestAttemptAsync(
                visit.SessionId, quote.OperationId, visit.SecondToken, CancellationToken.None);
            await wrongActor.Should().ThrowAsync<NotFoundException>();
        }

        var newQuote = () => CreateGuestQuoteAsync(visit, visit.FirstToken,
            ItemsQuote(Guid.NewGuid(), visit, ordinal: 2), disabledFeatures);
        await newQuote.Should().ThrowAsync<NotFoundException>();

        await using (var verifyQuote = fixture.CreateContext())
        {
            var nextAttempt = await verifyQuote.AccountPaymentAttempts.SingleAsync(
                value => value.OperationId == nextQuote.OperationId);
            nextAttempt.State.Should().Be(AccountPaymentState.Quoted);
        }

        await using (var update = fixture.CreateContext())
        {
            var attempt = await update.AccountPaymentAttempts.SingleAsync(value => value.OperationId == quote.OperationId);
            attempt.State = AccountPaymentState.Processing;
            attempt.Version++;
            attempt.StartedAt = FixedNow.UtcDateTime;
            attempt.ProviderSessionId = "provider-session-evidence";
            await update.SaveChangesAsync();
        }

        var release = () => ReleaseGuestAsync(visit, visit.FirstToken, quote.OperationId, expectedVersion: 3);
        await release.Should().ThrowAsync<ConflictException>()
            .WithMessage("*cannot be released locally*");

        await using var verify = fixture.CreateContext();
        (await verify.AccountPaymentAttempts.SingleAsync(value => value.OperationId == quote.OperationId))
            .State.Should().Be(AccountPaymentState.Processing);
    }

    private async Task<ReservationOutcome> TryReserveGuestAsync(
        SeededVisit visit, string token, Guid operationId)
    {
        try
        {
            await ReserveGuestAsync(visit, token, operationId);
            var participantId = token == visit.FirstToken ? visit.FirstParticipantId : visit.SecondParticipantId;
            return new(true, participantId, AccountPaymentActorKind.GuestParticipant, operationId, token);
        }
        catch (ConflictException)
        {
            var participantId = token == visit.FirstToken ? visit.FirstParticipantId : visit.SecondParticipantId;
            return new(false, participantId, AccountPaymentActorKind.GuestParticipant, operationId, token);
        }
    }

    private async Task<ReservationOutcome> TryReserveStaffAsync(
        SeededVisit visit, Guid operationId)
    {
        try
        {
            await ReserveStaffAsync(visit, operationId);
            return new(true, StaffActorId, AccountPaymentActorKind.Staff, operationId, null);
        }
        catch (ConflictException)
        {
            return new(false, StaffActorId, AccountPaymentActorKind.Staff, operationId, null);
        }
    }

    private async Task<AccountPaymentOperationDto> CreateGuestQuoteAsync(
        SeededVisit visit, string token, CreateAccountPaymentQuoteRequest request,
        ITenantFeatures? features = null)
    {
        await using var context = fixture.CreateContext();
        var activeFeatures = features ?? Features();
        return await new AccountPaymentQuoteService(context, Mock.Of<IAccountPaymentActorResolver>(),
                GuestAuthorization(context), GuestPolicy(activeFeatures), activeFeatures,
                Options.Create(new AccountPaymentSettings()), new FixedTimeProvider(FixedNow))
            .CreateGuestQuoteAsync(visit.SessionId, token, request, CancellationToken.None);
    }

    private async Task<AccountEqualSharePlanDto> CreateGuestPlanAsync(
        SeededVisit visit, string token, CreateAccountEqualSharePlanRequest request)
    {
        await using var context = fixture.CreateContext();
        var features = Features();
        return await new AccountEqualSharePlanService(context, Mock.Of<IAccountPaymentActorResolver>(),
                GuestAuthorization(context), GuestPolicy(features), features,
                Options.Create(new AccountPaymentSettings()), new FixedTimeProvider(FixedNow))
            .CreateGuestAsync(visit.SessionId, token, request, CancellationToken.None);
    }

    private async Task<AccountPaymentOperationDto> CreateStaffQuoteAsync(
        SeededVisit visit, CreateAccountPaymentQuoteRequest request)
    {
        await using var context = fixture.CreateContext();
        var features = Features();
        return await new AccountPaymentQuoteService(context, StaffActorResolver(),
                GuestAuthorization(context), GuestPolicy(features), features,
                Options.Create(new AccountPaymentSettings()), new FixedTimeProvider(FixedNow))
            .CreateQuoteAsync(visit.SessionId, request, CancellationToken.None);
    }

    private async Task<AccountPaymentOperationDto> ReserveGuestAsync(
        SeededVisit visit, string token, Guid operationId, ITenantFeatures? features = null,
        int expectedVersion = 1)
    {
        await using var context = fixture.CreateContext();
        var activeFeatures = features ?? Features();
        return await new AccountPaymentReservationService(context, Mock.Of<IAccountPaymentActorResolver>(),
                GuestAuthorization(context), GuestPolicy(activeFeatures), activeFeatures,
                Options.Create(new AccountPaymentSettings()), new FixedTimeProvider(FixedNow))
            .ReserveGuestAsync(visit.SessionId, operationId, token,
                new ReserveAccountPaymentRequest { ExpectedVersion = expectedVersion, ExpectedAccountRevision = 1 },
                CancellationToken.None);
    }

    private async Task<AccountPaymentOperationDto> ReleaseGuestAsync(
        SeededVisit visit, string token, Guid operationId, int expectedVersion)
    {
        await using var context = fixture.CreateContext();
        var features = Features();
        return await new AccountPaymentReservationService(context, Mock.Of<IAccountPaymentActorResolver>(),
                GuestAuthorization(context), GuestPolicy(features), features,
                Options.Create(new AccountPaymentSettings()), new FixedTimeProvider(FixedNow))
            .ReleaseGuestAsync(visit.SessionId, operationId, token,
                new ReleaseAccountPaymentRequest { ExpectedVersion = expectedVersion }, CancellationToken.None);
    }

    private async Task<AccountPaymentOperationDto> ReserveStaffAsync(
        SeededVisit visit, Guid operationId)
    {
        await using var context = fixture.CreateContext();
        var features = Features();
        return await new AccountPaymentReservationService(context, StaffActorResolver(),
                GuestAuthorization(context), GuestPolicy(features), features,
                Options.Create(new AccountPaymentSettings()), new FixedTimeProvider(FixedNow))
            .ReserveAsync(visit.SessionId, operationId,
                new ReserveAccountPaymentRequest { ExpectedVersion = 1, ExpectedAccountRevision = 1 },
                CancellationToken.None);
    }

    private AccountPaymentAccountReader AccountReader(
        RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext context, ITenantFeatures features) =>
        new(context, Mock.Of<IAccountPaymentActorResolver>(), GuestAuthorization(context),
            GuestPolicy(features), features, Options.Create(new AccountPaymentSettings()));

    private AccountPaymentOperationReader OperationReader(
        RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext context) =>
        new(context, Mock.Of<IAccountPaymentActorResolver>(), GuestAuthorization(context));

    private TableGuestParticipantPaymentAuthorization GuestAuthorization(
        RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext context) =>
        new TableGuestParticipantPaymentAuthorization(context, new FixedTimeProvider(FixedNow));

    private static GuestAccountPaymentPolicy GuestPolicy(ITenantFeatures features) => new(
        features,
        Mock.Of<ITenantModules>(value => value.IsEnabled(ModuleIds.OnlinePayments)),
        Mock.Of<IStripeGateway>(value => value.IsConfigured),
        Options.Create(new AccountOnlineContributionSettings { SettlementCurrency = "CHF" }));

    private static TenantFeatures Features(
        bool visits = true, bool accountPayments = true, bool guestPayments = true) =>
        new(Options.Create(new TenantFeatureSettings
        {
            TableGuestVisitsV1 = visits,
            TableAccountPaymentsV1 = accountPayments,
            TableGuestAccountPaymentsV1 = guestPayments
        }));

    private static readonly Guid StaffActorId = Guid.NewGuid();

    private static TestActorResolver StaffActorResolver() =>
        new TestActorResolver(StaffActorId);

    private async Task<SeededVisit> SeedAccountAsync()
    {
        var tableId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var admissionId = Guid.NewGuid();
        var firstParticipantId = Guid.NewGuid();
        var secondParticipantId = Guid.NewGuid();
        var firstToken = TableGuestCredentialCrypto.CreateParticipantToken();
        var secondToken = TableGuestCredentialCrypto.CreateParticipantToken();
        TableGuestCredentialCrypto.TryHashParticipantToken(firstToken, out var firstHash).Should().BeTrue();
        TableGuestCredentialCrypto.TryHashParticipantToken(secondToken, out var secondHash).Should().BeTrue();

        await using var context = fixture.CreateContext();
        context.Tables.Add(new Table
        {
            Id = tableId,
            TableNumber = "T-GUEST",
            QRCodeData = $"qr-{sessionId:N}",
            IsActive = true,
            MaxGuests = 4,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(GuestAccountPaymentFlowTests)
        });
        context.TableServiceSessions.Add(new TableServiceSession
        {
            Id = sessionId,
            TableId = tableId,
            Status = TableServiceSessionStatus.Open,
            Currency = "CHF",
            AccountRevision = 1,
            OpenedAt = FixedNow.UtcDateTime,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(GuestAccountPaymentFlowTests)
        });
        context.TableGuestAdmissions.Add(new TableGuestAdmission
        {
            Id = admissionId,
            ServiceSessionId = sessionId,
            CodeHash = TableGuestCredentialCrypto.HashAdmissionCode("123456789A"),
            ExpiresAt = FixedNow.UtcDateTime.AddHours(12),
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(GuestAccountPaymentFlowTests)
        });
        context.TableGuestParticipants.AddRange(
            Participant(firstParticipantId, sessionId, admissionId, firstHash),
            Participant(secondParticipantId, sessionId, admissionId, secondHash));
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"GUEST-{Guid.NewGuid():N}"[..14],
            Type = OrderType.DineIn,
            TableId = tableId,
            TableLabel = "T-GUEST",
            ServiceSessionId = sessionId,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 20m,
            Total = 20m,
            RemainingAmount = 20m,
            OrderDate = FixedNow.UtcDateTime,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(GuestAccountPaymentFlowTests)
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductName = "Guest share fixture",
            Quantity = 2,
            UnitPrice = 10m,
            ItemTotal = 20m,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(GuestAccountPaymentFlowTests)
        });
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return new SeededVisit(sessionId, order.Id, order.Items.Single().Id,
            firstParticipantId, secondParticipantId, firstToken, secondToken);
    }

    private static TableGuestParticipant Participant(
        Guid participantId, Guid sessionId, Guid admissionId, string tokenHash) => new()
        {
            Id = participantId,
            ServiceSessionId = sessionId,
            AdmissionId = admissionId,
            TokenHash = tokenHash,
            ExpiresAt = FixedNow.UtcDateTime.AddHours(2),
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(GuestAccountPaymentFlowTests)
        };

    private static CreateAccountPaymentQuoteRequest ItemsQuote(
        Guid operationId, SeededVisit visit, int ordinal) => new()
        {
            OperationId = operationId,
            ExpectedAccountRevision = 1,
            Mode = AccountPaymentMode.Items,
            PaymentMethod = PaymentMethod.OnlinePayment,
            SelectedUnits = [new AccountPaymentUnitSelection(visit.OrderId, visit.ItemId, ordinal)]
        };

    private static CreateAccountPaymentQuoteRequest EqualQuote(Guid operationId, Guid planId, int ordinal) => new()
    {
        OperationId = operationId,
        ExpectedAccountRevision = 1,
        Mode = AccountPaymentMode.Equal,
        PaymentMethod = PaymentMethod.OnlinePayment,
        EqualSharePlanId = planId,
        EqualShareOrdinal = ordinal
    };

    private sealed record SeededVisit(
        Guid SessionId, Guid OrderId, Guid ItemId, Guid FirstParticipantId,
        Guid SecondParticipantId, string FirstToken, string SecondToken);

    private sealed record ReservationOutcome(
        bool Success, Guid ActorId, AccountPaymentActorKind ActorKind, Guid OperationId, string? Token);

    private sealed class TestActorResolver(Guid actorId) : IAccountPaymentActorResolver
    {
        public AccountPaymentActor ResolveStaffActor() =>
            new(actorId, AccountPaymentActorKind.Staff, $"staff:{actorId:N}");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
