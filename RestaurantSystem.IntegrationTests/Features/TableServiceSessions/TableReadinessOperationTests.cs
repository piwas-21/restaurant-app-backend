using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.MarkTableReadyCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.OpenTableServiceSessionCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableServiceSessions;

[Collection("Database Lane 4")]
public sealed class TableReadinessOperationTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public TableReadinessOperationTests(DatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Successful_operation_replays_its_frozen_outcome_without_changing_a_new_visit()
    {
        var tableId = await SeedTableAsync();
        var operationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        await using (var context = _fixture.CreateContext())
        {
            var result = await ReadyHandler(context, staffId).Handle(
                NewCommand(tableId, operationId, 1), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Data!.ReadinessState.Should().Be(nameof(TableReadinessState.ReadyForGuests));
            result.Data.ReadinessVersion.Should().Be(2);
        }

        await using (var context = _fixture.CreateContext())
        {
            var table = await context.Tables.SingleAsync(value => value.Id == tableId);
            table.ReadinessState = TableReadinessState.NeedsReset;
            table.ReadinessVersion = 7;
            context.TableServiceSessions.Add(NewSession(tableId));
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var replay = await ReadyHandler(context, staffId).Handle(
                NewCommand(tableId, operationId, 1), CancellationToken.None);

            replay.Success.Should().BeTrue();
            replay.Data!.ReadinessState.Should().Be(nameof(TableReadinessState.ReadyForGuests));
            replay.Data.ReadinessVersion.Should().Be(2);
        }

        await using var verify = _fixture.CreateContext();
        var unchanged = await verify.Tables.SingleAsync(value => value.Id == tableId);
        unchanged.ReadinessState.Should().Be(TableReadinessState.NeedsReset);
        unchanged.ReadinessVersion.Should().Be(7);
        (await verify.TableReadyOperations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Rejected_operation_replays_the_original_refusal_after_the_blocker_is_resolved()
    {
        var tableId = await SeedTableAsync();
        var operationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        await using (var context = _fixture.CreateContext())
        {
            context.TableServiceSessions.Add(NewSession(tableId));
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var rejected = await ReadyHandler(context, staffId).Handle(
                NewCommand(tableId, operationId, 1), CancellationToken.None);

            rejected.Success.Should().BeFalse();
            rejected.ErrorCode.Should().Be(ErrorCodes.TableReadinessVisitOpen);
        }

        await using (var context = _fixture.CreateContext())
        {
            var session = await context.TableServiceSessions.SingleAsync();
            session.Status = TableServiceSessionStatus.Closed;
            session.ClosedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var replay = await ReadyHandler(context, staffId).Handle(
                NewCommand(tableId, operationId, 1), CancellationToken.None);

            replay.Success.Should().BeFalse();
            replay.ErrorCode.Should().Be(ErrorCodes.TableReadinessVisitOpen);
        }

        await using var verify = _fixture.CreateContext();
        var saved = await verify.TableReadyOperations.SingleAsync();
        saved.Succeeded.Should().BeFalse();
        saved.OutcomeErrorCode.Should().Be(ErrorCodes.TableReadinessVisitOpen);
        saved.OutcomeState.Should().Be(TableReadinessState.NeedsReset);
        saved.OutcomeReadinessVersion.Should().Be(1);
    }

    [Fact]
    public async Task Legacy_open_visit_without_stable_table_id_is_an_ambiguous_readiness_blocker()
    {
        var tableId = await SeedTableAsync("12");
        await using (var context = _fixture.CreateContext())
        {
            context.TableServiceSessions.Add(new TableServiceSession
            {
                Id = Guid.NewGuid(),
                TableNumber = 12,
                TableId = null,
                Status = TableServiceSessionStatus.Open,
                Version = 1,
                AccountRevision = 1,
                OpenedAt = DateTime.UtcNow,
                CreatedBy = nameof(TableReadinessOperationTests),
            });
            await context.SaveChangesAsync();
        }

        await using var handlerContext = _fixture.CreateContext();
        var result = await ReadyHandler(handlerContext).Handle(
            NewCommand(tableId, Guid.NewGuid(), 1), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionAmbiguous);
        (await handlerContext.TableReadyOperations.SingleAsync()).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Reusing_operation_id_with_different_actor_or_expected_version_is_rejected()
    {
        var tableId = await SeedTableAsync();
        var operationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await using (var context = _fixture.CreateContext())
        {
            (await ReadyHandler(context, actorId).Handle(
                NewCommand(tableId, operationId, 1), CancellationToken.None)).Success.Should().BeTrue();
        }

        await using (var context = _fixture.CreateContext())
        {
            var wrongActor = await ReadyHandler(context, Guid.NewGuid()).Handle(
                NewCommand(tableId, operationId, 1), CancellationToken.None);
            var wrongVersion = await ReadyHandler(context, actorId).Handle(
                NewCommand(tableId, operationId, 2), CancellationToken.None);

            wrongActor.ErrorCode.Should().Be(ErrorCodes.TableReadinessOperationMismatch);
            wrongVersion.ErrorCode.Should().Be(ErrorCodes.TableReadinessOperationMismatch);
        }

        await using var verify = _fixture.CreateContext();
        (await verify.TableReadyOperations.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    public async Task Admin_cashier_and_server_can_mark_a_table_ready(UserRole role)
    {
        var tableId = await SeedTableAsync();
        await using var context = _fixture.CreateContext();

        var result = await ReadyHandler(context, Guid.NewGuid(), role).Handle(
            NewCommand(tableId, Guid.NewGuid(), 1), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.ReadinessState.Should().Be(nameof(TableReadinessState.ReadyForGuests));
    }

    [Fact]
    public async Task Nonstaff_and_feature_off_requests_do_not_create_operation_receipts()
    {
        var tableId = await SeedTableAsync();
        var command = NewCommand(tableId, Guid.NewGuid(), 1);
        await using (var context = _fixture.CreateContext())
        {
            var nonstaff = await ReadyHandler(context, Guid.NewGuid(), UserRole.Customer)
                .Handle(command, CancellationToken.None);
            nonstaff.ErrorCode.Should().Be(ErrorCodes.TableReadinessStaffRequired);
        }

        await using (var context = _fixture.CreateContext())
        {
            var disabled = new MarkTableReadyCommandHandler(
                context,
                StaffUser(),
                Mock.Of<ITenantFeatures>(),
                settings: Options.Create(new TableServiceSessionSettings()));
            var result = await disabled.Handle(command, CancellationToken.None);
            result.ErrorCode.Should().Be(ErrorCodes.TableReadinessFeatureDisabled);
        }

        await using var verify = _fixture.CreateContext();
        (await verify.TableReadyOperations.CountAsync()).Should().Be(0);
        var unchanged = await verify.Tables.SingleAsync(value => value.Id == tableId);
        unchanged.ReadinessState.Should().Be(TableReadinessState.NeedsReset);
        unchanged.ReadinessVersion.Should().Be(1);
    }

    [Fact]
    public async Task Existing_open_visit_replay_precedes_needs_reset_refusal()
    {
        var tableId = await SeedTableAsync();
        var session = NewSession(tableId);
        await using var context = _fixture.CreateContext();
        context.TableServiceSessions.Add(session);
        await context.SaveChangesAsync();
        var reader = new Mock<ITableServiceSessionReader>();
        reader.Setup(value => value.ReadAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableServiceSessionDto
            {
                ServiceSessionId = session.Id,
                TableId = tableId,
                Status = nameof(TableServiceSessionStatus.Open),
                Version = 1,
            });
        var handler = new OpenTableServiceSessionCommandHandler(
            context,
            StaffUser(),
            reader.Object,
            new TableIdentityResolver(context),
            Options.Create(new TableServiceSessionSettings()),
            features: EnabledReadiness());

        var result = await handler.Handle(
            new OpenTableServiceSessionCommand { TableId = tableId }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.ServiceSessionId.Should().Be(session.Id);
    }

    [Fact]
    public async Task Readiness_flag_on_refuses_a_new_visit_until_the_table_is_ready()
    {
        var tableId = await SeedTableAsync();
        await using var context = _fixture.CreateContext();
        var handler = new OpenTableServiceSessionCommandHandler(
            context,
            StaffUser(),
            Mock.Of<ITableServiceSessionReader>(),
            new TableIdentityResolver(context),
            Options.Create(new TableServiceSessionSettings()),
            features: EnabledReadiness());

        var result = await handler.Handle(
            new OpenTableServiceSessionCommand { TableId = tableId }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TableReadinessNotAvailable);
        (await context.TableServiceSessions.CountAsync()).Should().Be(0);
        (await context.Tables.SingleAsync(value => value.Id == tableId)).ReadinessState
            .Should().Be(TableReadinessState.NeedsReset);
    }

    [Fact]
    public async Task Readiness_flag_off_preserves_legacy_open_behavior()
    {
        var tableId = await SeedTableAsync();
        await using var context = _fixture.CreateContext();
        var reader = new Mock<ITableServiceSessionReader>();
        reader.Setup(value => value.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((sessionId, _) => Task.FromResult<TableServiceSessionDto?>(
                new TableServiceSessionDto
                {
                    ServiceSessionId = sessionId,
                    TableId = tableId,
                    Status = nameof(TableServiceSessionStatus.Open),
                    Version = 1,
                }));
        var handler = new OpenTableServiceSessionCommandHandler(
            context,
            StaffUser(),
            reader.Object,
            new TableIdentityResolver(context),
            Options.Create(new TableServiceSessionSettings()),
            features: Mock.Of<ITenantFeatures>());

        var result = await handler.Handle(
            new OpenTableServiceSessionCommand { TableId = tableId }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.ServiceSessionId.Should().NotBeEmpty();
        (await context.Tables.SingleAsync(value => value.Id == tableId)).ReadinessState
            .Should().Be(TableReadinessState.NeedsReset);
    }

    private async Task<Guid> SeedTableAsync(string? tableNumber = null)
    {
        var tableId = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        context.Tables.Add(new Table
        {
            Id = tableId,
            TableNumber = tableNumber ?? $"R-{Guid.NewGuid():N}",
            MaxGuests = 4,
            IsActive = true,
            ReadinessState = TableReadinessState.NeedsReset,
            ReadinessVersion = 1,
            CreatedBy = nameof(TableReadinessOperationTests),
        });
        await context.SaveChangesAsync();
        return tableId;
    }

    private static TableServiceSession NewSession(Guid tableId) => new()
    {
        Id = Guid.NewGuid(),
        TableId = tableId,
        Status = TableServiceSessionStatus.Open,
        Version = 1,
        AccountRevision = 1,
        OpenedAt = DateTime.UtcNow,
        CreatedBy = nameof(TableReadinessOperationTests),
    };

    private static MarkTableReadyCommand NewCommand(Guid tableId, Guid operationId, int expectedVersion) => new()
    {
        TableId = tableId,
        OperationId = operationId,
        ExpectedReadinessVersion = expectedVersion,
    };

    private static MarkTableReadyCommandHandler ReadyHandler(
        ApplicationDbContext context,
        Guid? staffId = null,
        UserRole role = UserRole.Server) =>
        new(context, StaffUser(staffId, role), EnabledReadiness(),
            settings: Options.Create(new TableServiceSessionSettings()));

    private static ITenantFeatures EnabledReadiness() =>
        Mock.Of<ITenantFeatures>(features => features.TableVisitReadinessV1);

    private static ICurrentUserService StaffUser(Guid? staffId = null, UserRole role = UserRole.Server)
    {
        var identity = new Mock<ICurrentUserService>();
        identity.SetupGet(user => user.IsAuthenticated).Returns(true);
        identity.SetupGet(user => user.UserId).Returns(staffId ?? Guid.NewGuid());
        identity.SetupGet(user => user.Role).Returns(role);
        identity.Setup(user => user.GetAuditIdentifier()).Returns("readiness-test");
        return identity.Object;
    }
}
