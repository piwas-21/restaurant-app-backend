using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.CloseTableServiceSessionCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class TableGuestVisitAuthorizationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly DatabaseFixture _fixture;

    public TableGuestVisitAuthorizationTests(DatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Admission_code_and_participant_are_bound_to_the_exact_table_visit()
    {
        var first = await SeedVisitAsync("T-01", "qr-exact-first");
        var second = await SeedVisitAsync("T-02", "qr-exact-second");
        await using var context = _fixture.CreateContext();
        var service = AdmissionService(context);
        var code = await service.CreateCodeAsync(first.SessionId, CancellationToken.None);
        var rotatedCode = await service.CreateCodeAsync(first.SessionId, CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(() => service.JoinAsync(
            second.QrCode, rotatedCode.AdmissionCode, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.JoinAsync(
            first.QrCode, code.AdmissionCode, CancellationToken.None));
        var joined = await service.JoinAsync(first.QrCode, rotatedCode.AdmissionCode, CancellationToken.None);

        joined.ServiceSessionId.Should().Be(first.SessionId);
        await using var verify = _fixture.CreateContext();
        var stored = await verify.TableGuestParticipants.SingleAsync();
        stored.ServiceSessionId.Should().Be(first.SessionId);
        stored.TokenHash.Should().NotBe(joined.ParticipantToken);
        TableGuestCredentialCrypto.TryHashParticipantToken(joined.ParticipantToken, out var expectedHash)
            .Should().BeTrue();
        stored.TokenHash.Should().Be(expectedHash);

        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var privateEmail = "guest-private-email@example.invalid";
        var privateNote = "private order note";
        var privateInstruction = "private kitchen instruction";
        var bill = new RestaurantSystem.Api.Features.Orders.Dtos.TableBillDto
        {
            ServiceSessionId = first.SessionId,
            AccountRevision = 7,
            TableLabel = "T-01",
            Currency = "CHF",
            Total = 12m,
            TotalPaid = 3m,
            Remaining = 9m,
            Orders =
            [
                new RestaurantSystem.Api.Features.Orders.Dtos.OrderDto
                {
                    Id = orderId,
                    OrderNumber = "TG-0001",
                    Status = "Preparing",
                    PaymentStatus = "PartiallyPaid",
                    OrderDate = FixedNow.UtcDateTime,
                    Total = 12m,
                    TotalPaid = 3m,
                    RemainingAmount = 9m,
                    CustomerEmail = privateEmail,
                    Notes = privateNote,
                    GuestStatusToken = "private-status-token",
                }
            ],
            AccountItems =
            [
                new RestaurantSystem.Api.Features.Orders.Dtos.TableBillAccountItemDto
                {
                    OrderId = orderId,
                    OrderNumber = "TG-0001",
                    OrderItemId = itemId,
                    UnitCount = 1,
                    ItemSnapshot = new RestaurantSystem.Api.Features.Orders.Dtos.OrderItemDto
                    {
                        Id = itemId,
                        ProductName = "Soup",
                        Quantity = 1,
                        UnitPrice = 12m,
                        ItemTotal = 12m,
                        SpecialInstructions = privateInstruction,
                    }
                }
            ]
        };
        var reader = AccountReader(verify, bill);
        await Assert.ThrowsAsync<NotFoundException>(() => reader.ReadAsync(
            second.SessionId, joined.ParticipantToken, CancellationToken.None));
        var account = await reader.ReadAsync(first.SessionId, joined.ParticipantToken, CancellationToken.None);
        account.ServiceSessionId.Should().Be(first.SessionId);
        account.TableLabel.Should().Be("T-01");
        account.AccountRevision.Should().Be(7);
        account.Total.Should().Be(12m);
        account.Items.Should().ContainSingle(item => item.UnitCount == 1 && item.Item.ItemId == itemId);
        var serializedAccount = JsonSerializer.Serialize(account);
        serializedAccount.Should().NotContain(privateEmail);
        serializedAccount.Should().NotContain(privateNote);
        serializedAccount.Should().NotContain(privateInstruction);
        serializedAccount.Should().NotContain("private-status-token");
    }

    [Fact]
    public async Task Closing_visit_revokes_credentials_before_the_table_is_reused()
    {
        var first = await SeedVisitAsync("T-11", "qr-rotating");
        string oldAdmissionCode;
        string oldParticipantToken;
        await using (var context = _fixture.CreateContext())
        {
            var service = AdmissionService(context);
            oldAdmissionCode = (await service.CreateCodeAsync(first.SessionId, CancellationToken.None)).AdmissionCode;
            oldParticipantToken = (await service.JoinAsync(
                first.QrCode, oldAdmissionCode, CancellationToken.None)).ParticipantToken;
        }

        await using (var context = _fixture.CreateContext())
        {
            var reader = new Mock<ITableServiceSessionReader>();
            reader.Setup(value => value.ReadAsync(first.SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TableServiceSessionDto
                {
                    ServiceSessionId = first.SessionId,
                    Status = nameof(TableServiceSessionStatus.Closed),
                });
            var closer = new CloseTableServiceSessionCommandHandler(
                context, reader.Object, new TableGuestVisitRevoker(context),
                new FixedTimeProvider(FixedNow),
                Options.Create(new TableServiceSessionSettings()));
            var result = await closer.Handle(new CloseTableServiceSessionCommand
            {
                ServiceSessionId = first.SessionId,
                ExpectedVersion = 1,
            }, CancellationToken.None);
            result.Success.Should().BeTrue();
        }

        await using (var context = _fixture.CreateContext())
        {
            var table = await context.Tables.SingleAsync(value => value.Id == first.TableId);
            table.ReadinessState.Should().Be(TableReadinessState.NeedsReset);
            table.ReadinessVersion.Should().Be(4);
            table.ReadinessState = TableReadinessState.ReadyForGuests;
            table.ReadinessVersion++;
            await context.SaveChangesAsync();
        }

        var nextVisit = await SeedOpenSessionAsync(first.TableId);
        await using (var context = _fixture.CreateContext())
        {
            var reader = new Mock<ITableServiceSessionReader>();
            reader.Setup(value => value.ReadAsync(first.SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TableServiceSessionDto
                {
                    ServiceSessionId = first.SessionId,
                    Status = nameof(TableServiceSessionStatus.Closed),
                });
            var closer = new CloseTableServiceSessionCommandHandler(
                context, reader.Object, new TableGuestVisitRevoker(context),
                new FixedTimeProvider(FixedNow),
                Options.Create(new TableServiceSessionSettings()));
            (await closer.Handle(new CloseTableServiceSessionCommand
            {
                ServiceSessionId = first.SessionId,
                ExpectedVersion = 1,
            }, CancellationToken.None)).Success.Should().BeTrue();
        }

        await using var nextContext = _fixture.CreateContext();
        var nextTable = await nextContext.Tables.SingleAsync(value => value.Id == first.TableId);
        nextTable.ReadinessState.Should().Be(TableReadinessState.ReadyForGuests,
            "a close retry from the previous visit cannot reset a table now serving another party");
        nextTable.ReadinessVersion.Should().Be(5);
        (await nextContext.TableServiceSessions.SingleAsync(value => value.Id == nextVisit))
            .Status.Should().Be(TableServiceSessionStatus.Open);
        var serviceForNextVisit = AdmissionService(nextContext);
        await Assert.ThrowsAsync<NotFoundException>(() => serviceForNextVisit.JoinAsync(
            first.QrCode, oldAdmissionCode, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => AccountReader(nextContext).ReadAsync(
            nextVisit, oldParticipantToken, CancellationToken.None));

        var newCode = await serviceForNextVisit.CreateCodeAsync(nextVisit, CancellationToken.None);
        (await serviceForNextVisit.JoinAsync(first.QrCode, newCode.AdmissionCode, CancellationToken.None))
            .ServiceSessionId.Should().Be(nextVisit);

        await using var verify = _fixture.CreateContext();
        var revoked = await verify.TableGuestParticipants.SingleAsync(
            value => value.ServiceSessionId == first.SessionId);
        revoked.RevokedAt.Should().Be(FixedNow.UtcDateTime);
        (await verify.TableGuestAdmissions.SingleAsync(
            value => value.ServiceSessionId == first.SessionId)).RevokedAt.Should().Be(FixedNow.UtcDateTime);
    }

    private TableGuestAdmissionService AdmissionService(ApplicationDbContext context) =>
        new(context, GuestFeatures(), CurrentUser(), Options.Create(new TableGuestVisitSettings()),
            new FixedTimeProvider(FixedNow));

    private TableGuestAccountReader AccountReader(
        ApplicationDbContext context, RestaurantSystem.Api.Features.Orders.Dtos.TableBillDto? bill = null)
    {
        var bills = new Mock<ITableBillAssembler>();
        bills.Setup(value => value.AssembleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bill);
        return new TableGuestAccountReader(context, GuestFeatures(), bills.Object, new FixedTimeProvider(FixedNow));
    }

    private static ITenantFeatures GuestFeatures() =>
        Mock.Of<ITenantFeatures>(features => features.TableGuestVisitsV1);

    private static ICurrentUserService CurrentUser() =>
        Mock.Of<ICurrentUserService>(user => user.GetAuditIdentifier() == nameof(TableGuestVisitAuthorizationTests));

    private async Task<(Guid TableId, Guid SessionId, string QrCode)> SeedVisitAsync(string label, string qrCode)
    {
        var tableId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        context.Tables.Add(new Table
        {
            Id = tableId,
            TableNumber = label,
            QRCodeData = qrCode,
            IsActive = true,
            MaxGuests = 4,
            ReadinessState = TableReadinessState.ReadyForGuests,
            ReadinessVersion = 3,
            CreatedAt = FixedNow.UtcDateTime,
            CreatedBy = nameof(TableGuestVisitAuthorizationTests),
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
            CreatedBy = nameof(TableGuestVisitAuthorizationTests),
        });
        await context.SaveChangesAsync();
        return (tableId, sessionId, qrCode);
    }

    private async Task<Guid> SeedOpenSessionAsync(Guid tableId)
    {
        var sessionId = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        context.TableServiceSessions.Add(new TableServiceSession
        {
            Id = sessionId,
            TableId = tableId,
            Status = TableServiceSessionStatus.Open,
            Version = 1,
            AccountRevision = 1,
            OpenedAt = FixedNow.UtcDateTime.AddMinutes(1),
            CreatedAt = FixedNow.UtcDateTime.AddMinutes(1),
            CreatedBy = nameof(TableGuestVisitAuthorizationTests),
        });
        await context.SaveChangesAsync();
        return sessionId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
