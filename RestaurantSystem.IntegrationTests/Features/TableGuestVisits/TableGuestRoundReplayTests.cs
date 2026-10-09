using System.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderFromBasketCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class TableGuestRoundReplayTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly DatabaseFixture _fixture;

    public TableGuestRoundReplayTests(DatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lost_response_retry_replays_before_the_cleared_basket_is_read()
    {
        var context = NewRoundContext();
        var expectedOrder = new OrderDto { Id = Guid.NewGuid(), OrderNumber = "GUEST-1" };
        var replay = ApiResponse<OrderDto>.SuccessWithData(expectedOrder, "Guest round already created");
        var operations = new Mock<ITableGuestRoundOperationStore>();
        operations.Setup(value => value.FindReplayBeforeBasketAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(replay);
        var basket = new Mock<IBasketService>();
        var translator = new Mock<IBasketToOrderTranslator>();
        var currentUser = new Mock<ICurrentUserService>();
        var orderTypes = new Mock<IOrderTypeConfigurationService>();
        orderTypes.Setup(value => value.GetEnabledOrderTypesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var mediator = new CustomMediator(Mock.Of<IServiceProvider>());
        var handler = new CreateOrderFromBasketCommandHandler(
            basket.Object, translator.Object, currentUser.Object, mediator, orderTypes.Object, operations.Object);
        var command = new CreateOrderFromBasketCommand
        {
            SessionId = "basket-capability",
            GuestRoundContext = context,
            Type = OrderType.DineIn,
        };

        var response = await handler.Handle(command, CancellationToken.None);

        response.Data.Should().BeSameAs(expectedOrder);
        response.Message.Should().Be("Guest round already created");
        basket.Verify(value => value.GetBasketAsync(It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        translator.Verify(value => value.Translate(It.IsAny<IEnumerable<RestaurantSystem.Api.Features.Basket.Dtos.BasketItemDto>>()), Times.Never);
        orderTypes.Verify(value => value.GetEnabledOrderTypesAsync(It.IsAny<CancellationToken>()), Times.Never,
            "a committed operation remains replayable even after DineIn becomes unavailable");
    }

    [Fact]
    public async Task Changed_live_basket_is_rejected_before_order_translation_or_creation()
    {
        var context = NewRoundContext();
        var operations = new Mock<ITableGuestRoundOperationStore>();
        operations.Setup(value => value.FindReplayBeforeBasketAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiResponse<OrderDto>?)null);
        var basket = new Mock<IBasketService>();
        basket.Setup(value => value.GetBasketAsync("basket-capability", It.IsAny<Guid?>()))
            .ReturnsAsync(new BasketDto
            {
                PurchaseFingerprint = new string('B', 64),
                Items = [new BasketItemDto
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = 1,
                    UnitPrice = 5m,
                    ItemTotal = 5m,
                }],
            });
        var translator = new Mock<IBasketToOrderTranslator>();
        var currentUser = new Mock<ICurrentUserService>();
        var orderTypes = new Mock<IOrderTypeConfigurationService>();
        orderTypes.Setup(value => value.GetEnabledOrderTypesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([OrderType.DineIn]);
        var handler = new CreateOrderFromBasketCommandHandler(
            basket.Object, translator.Object, currentUser.Object,
            new CustomMediator(Mock.Of<IServiceProvider>()), orderTypes.Object, operations.Object);

        var error = await Record.ExceptionAsync(() => handler.Handle(new CreateOrderFromBasketCommand
        {
            SessionId = "basket-capability",
            Type = OrderType.DineIn,
            GuestRoundContext = context,
        }, CancellationToken.None));

        error.Should().BeOfType<BadRequestException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionStale);
        basket.Verify(value => value.GetBasketAsync("basket-capability", It.IsAny<Guid?>()), Times.Once);
        translator.Verify(value => value.Translate(It.IsAny<IEnumerable<BasketItemDto>>()), Times.Never);
    }

    [Fact]
    public async Task Unavailable_guest_DineIn_is_rejected_before_basket_read_or_translation()
    {
        var context = NewRoundContext();
        var operations = new Mock<ITableGuestRoundOperationStore>();
        operations.Setup(value => value.FindReplayBeforeBasketAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiResponse<OrderDto>?)null);
        var basket = new Mock<IBasketService>();
        var translator = new Mock<IBasketToOrderTranslator>();
        var currentUser = new Mock<ICurrentUserService>();
        var orderTypes = new Mock<IOrderTypeConfigurationService>();
        orderTypes.Setup(value => value.GetEnabledOrderTypesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var handler = new CreateOrderFromBasketCommandHandler(
            basket.Object, translator.Object, currentUser.Object,
            new CustomMediator(Mock.Of<IServiceProvider>()), orderTypes.Object, operations.Object);

        var error = await Record.ExceptionAsync(() => handler.Handle(new CreateOrderFromBasketCommand
        {
            SessionId = "basket-capability",
            Type = OrderType.DineIn,
            GuestRoundContext = context,
        }, CancellationToken.None));

        error.Should().BeOfType<BadRequestException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.OrderTypeNotAvailable);
        error!.Message.Should().Be(
            "Dine-in ordering is currently unavailable. Your table visit and basket are still saved. Try again later or ask staff.");
        basket.Verify(value => value.GetBasketAsync(It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        translator.Verify(value => value.Translate(It.IsAny<IEnumerable<BasketItemDto>>()), Times.Never);
    }

    [Fact]
    public async Task Concurrent_duplicate_rounds_serialize_on_the_visit_and_replay_one_committed_order()
    {
        var visit = await SeedVisitAsync();
        var context = NewRoundContext(visit.SessionId, visit.TokenHash);
        var projector = new Mock<IOrderResponseProjector>();
        projector.Setup(value => value.ProjectAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order order, CancellationToken _) => new OrderDto
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
            });
        using var provider = CreateServices(projector.Object);
        await using var firstScope = provider.CreateAsyncScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var firstStore = firstScope.ServiceProvider.GetRequiredService<ITableGuestRoundOperationStore>();
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted);

        var firstAttempt = await firstStore.PrepareUnderLockAsync(context, CancellationToken.None);
        firstAttempt.Replay.Should().BeNull();
        firstAttempt.Participant!.Id.Should().Be(visit.ParticipantId);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "GUEST-RACE",
            Type = OrderType.DineIn,
            TableId = visit.TableId,
            TableLabel = "T-RACE",
            ServiceSessionId = visit.SessionId,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 8m,
            Total = 8m,
            RemainingAmount = 8m,
            OrderDate = FixedNow.UtcDateTime,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestRoundReplayTests),
        };
        firstContext.Orders.Add(order);
        firstAttempt.Session.RecordAccountChange();
        firstContext.TableGuestRoundOperations.Add(firstStore.CreateOperation(
            context, visit.ParticipantId, order.Id, nameof(TableGuestRoundReplayTests), FixedNow.UtcDateTime));
        await firstContext.SaveChangesAsync();

        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAttemptTask = Task.Run(async () =>
        {
            await using var secondScope = provider.CreateAsyncScope();
            var secondContext = secondScope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
            var secondStore = secondScope.ServiceProvider.GetRequiredService<ITableGuestRoundOperationStore>();
            await using var secondTransaction = await secondContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted);
            secondStarted.SetResult();
            var result = await secondStore.PrepareUnderLockAsync(context, CancellationToken.None);
            await secondTransaction.CommitAsync();
            return result;
        });
        await secondStarted.Task;
        await firstTransaction.CommitAsync();

        var secondAttempt = await secondAttemptTask;
        secondAttempt.Participant.Should().BeNull();
        secondAttempt.Replay.Should().NotBeNull();
        secondAttempt.Replay!.Data!.Id.Should().Be(order.Id);
        secondAttempt.Replay.Message.Should().Be("Guest round already created");

        await using var verify = _fixture.CreateContext();
        (await verify.Orders.CountAsync(value => value.ServiceSessionId == visit.SessionId)).Should().Be(1);
        (await verify.TableGuestRoundOperations.CountAsync(value => value.ServiceSessionId == visit.SessionId)).Should().Be(1);
        (await verify.TableServiceSessions.Where(value => value.Id == visit.SessionId)
            .Select(value => value.AccountRevision).SingleAsync()).Should().Be(2);
        projector.Verify(value => value.ProjectAsync(It.Is<Order>(row => row.Id == order.Id), It.IsAny<CancellationToken>()),
            Times.Once);

        var changedReview = context with { ExpectedBasketFingerprint = new string('B', 64) };
        await using var conflictScope = provider.CreateAsyncScope();
        var conflictContext = conflictScope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var conflictStore = conflictScope.ServiceProvider.GetRequiredService<ITableGuestRoundOperationStore>();
        await using var conflictTransaction = await conflictContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted);
        var replayConflict = await Record.ExceptionAsync(() =>
            conflictStore.PrepareUnderLockAsync(changedReview, CancellationToken.None));
        replayConflict.Should().BeOfType<BadRequestException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionStale);
    }

    private ServiceProvider CreateServices(IOrderResponseProjector projector)
    {
        var services = new ServiceCollection();
        services.AddTableGuestVisitServices();
        services.AddScoped(_ => _fixture.CreateContext());
        services.AddSingleton<ITenantFeatures>(Mock.Of<ITenantFeatures>(features => features.TableGuestVisitsV1));
        services.AddSingleton(projector);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
        return services.BuildServiceProvider();
    }

    private async Task<(Guid TableId, Guid SessionId, Guid ParticipantId, string TokenHash)> SeedVisitAsync()
    {
        var tableId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var admissionId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var token = TableGuestCredentialCrypto.CreateParticipantToken();
        TableGuestCredentialCrypto.TryHashParticipantToken(token, out var tokenHash).Should().BeTrue();
        await using var context = _fixture.CreateContext();
        context.Tables.Add(new Table
        {
            Id = tableId,
            TableNumber = "T-RACE",
            QRCodeData = "qr-race",
            IsActive = true,
            MaxGuests = 4,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestRoundReplayTests),
        });
        context.TableServiceSessions.Add(new TableServiceSession
        {
            Id = sessionId,
            TableId = tableId,
            Status = TableServiceSessionStatus.Open,
            Version = 1,
            AccountRevision = 1,
            OpenedAt = FixedNow.UtcDateTime,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestRoundReplayTests),
        });
        context.TableGuestAdmissions.Add(new TableGuestAdmission
        {
            Id = admissionId,
            ServiceSessionId = sessionId,
            CodeHash = TableGuestCredentialCrypto.HashAdmissionCode("123456789A"),
            ExpiresAt = FixedNow.UtcDateTime.AddHours(12),
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestRoundReplayTests),
        });
        context.TableGuestParticipants.Add(new TableGuestParticipant
        {
            Id = participantId,
            ServiceSessionId = sessionId,
            AdmissionId = admissionId,
            TokenHash = tokenHash,
            ExpiresAt = FixedNow.UtcDateTime.AddHours(12),
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestRoundReplayTests),
        });
        await context.SaveChangesAsync();
        return (tableId, sessionId, participantId, tokenHash);
    }

    private static TableGuestRoundContext NewRoundContext(
        Guid? sessionId = null, string? tokenHash = null) => new(
        sessionId ?? Guid.NewGuid(),
        Guid.NewGuid(),
        1,
        tokenHash ?? new string('A', 64),
        TableGuestCredentialCrypto.HashBasketSession("basket-capability"),
        new string('A', 64));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
