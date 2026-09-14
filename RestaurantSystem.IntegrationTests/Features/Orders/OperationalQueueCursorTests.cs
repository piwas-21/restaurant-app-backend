using FluentAssertions;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>Non-Docker tests for the authenticated, tenant-bound operational queue cursor.</summary>
public sealed class OperationalQueueCursorTests
{
    private static readonly Guid PositionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void ProtectAndRead_RoundTripsAnOpaqueCursor()
    {
        var cursor = CreateCursor("tenant-a");
        var value = cursor.Protect(new OperationalQueueCursorRequest
        {
            Mode = OperationalQueueSyncModes.Snapshot,
            FilterHash = new string('a', 64),
            UpperSequence = 12,
            LowerSequence = 0,
            Position = "123",
            PositionId = PositionId,
            Page = 1,
            PageSize = 25,
            TotalCount = 30,
        });

        value.Should().NotContain("tenant-a");
        var payload = cursor.Read(value);
        payload.Mode.Should().Be(OperationalQueueSyncModes.Snapshot);
        payload.UpperSequence.Should().Be(12);
        payload.PositionId.Should().Be(PositionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-protected-cursor")]
    public void Read_MalformedCursor_UsesStableInvalidError(string value)
    {
        var act = () => CreateCursor("tenant-a").Read(value);

        act.Should().Throw<BadRequestException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.InvalidOperationalQueueCursor);
    }

    [Fact]
    public void Read_TamperedCursor_UsesStableInvalidError()
    {
        var cursor = CreateCursor("tenant-a");
        var value = cursor.Protect(new OperationalQueueCursorRequest
        {
            Mode = OperationalQueueSyncModes.Watermark,
            FilterHash = new string('b', 64),
            UpperSequence = 12,
            Page = 1,
            PageSize = 25,
            TotalCount = 0,
        });
        var tampered = value[..^1] + (value[^1] == 'A' ? 'B' : 'A');

        var act = () => cursor.Read(tampered);

        act.Should().Throw<BadRequestException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.InvalidOperationalQueueCursor);
    }

    [Fact]
    public void Read_ExpiredCursor_UsesStableExpiredError()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-09-12T12:00:00Z"));
        var cursor = CreateCursor("tenant-a", clock);
        var value = cursor.Protect(new OperationalQueueCursorRequest
        {
            Mode = OperationalQueueSyncModes.Watermark,
            FilterHash = new string('c', 64),
            UpperSequence = 12,
            Page = 1,
            PageSize = 25,
            TotalCount = 0,
        });
        clock.Current = clock.Current.AddMinutes(16);

        var act = () => cursor.Read(value);

        act.Should().Throw<BadRequestException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.ExpiredOperationalQueueCursor);
    }

    [Fact]
    public void Read_CursorFromAnotherTenant_UsesStableInvalidError()
    {
        var provider = new EphemeralDataProtectionProvider(NullLoggerFactory.Instance);
        var value = CreateCursor("tenant-a", provider).Protect(new OperationalQueueCursorRequest
        {
            Mode = OperationalQueueSyncModes.Watermark,
            FilterHash = new string('d', 64),
            UpperSequence = 12,
            Page = 1,
            PageSize = 25,
            TotalCount = 0,
        });

        var act = () => CreateCursor("tenant-b", provider).Read(value);

        act.Should().Throw<BadRequestException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.InvalidOperationalQueueCursor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Read_InvalidFilterHash_UsesStableInvalidError(string? filterHash)
    {
        var provider = new EphemeralDataProtectionProvider(NullLoggerFactory.Instance);
        var value = ProtectJson(provider, new
        {
            Version = 1,
            Mode = OperationalQueueSyncModes.Watermark,
            TenantKey = "tenant-a",
            FilterHash = filterHash,
            UpperSequence = 12L,
            LowerSequence = 0L,
            Position = (string?)null,
            PositionId = (Guid?)null,
            Page = 1,
            PageSize = 25,
            TotalCount = 0,
            IssuedAtTicks = DateTime.UtcNow.Ticks,
            ExpiresAtTicks = DateTime.UtcNow.AddMinutes(15).Ticks,
        });

        var act = () => CreateCursor("tenant-a", provider).Read(value);

        act.Should().Throw<BadRequestException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.InvalidOperationalQueueCursor);
    }

    [Fact]
    public void Read_ProtectedPayloadFormatException_UsesStableInvalidError()
    {
        var act = () => new OperationalQueueCursor(
            new FormatExceptionProvider(),
            Options.Create(new OperationalQueueSyncOptions { TenantKey = "tenant-a" }),
            TimeProvider.System).Read("AQ==");

        act.Should().Throw<BadRequestException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.InvalidOperationalQueueCursor);
    }

    [Fact]
    public async Task Handle_NonOperationalSyncCursor_UsesStableInvalidError()
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().Options);
        var caller = new Mock<ICurrentUserService>();
        var clock = new Mock<ITenantClock>();
        var mapping = new Mock<IOrderMappingService>();
        var cursor = CreateCursor("tenant-a");
        var sync = new OperationalQueueSyncReader(
            context, caller.Object, clock.Object, mapping.Object, cursor,
            NullLogger<OperationalQueueSyncReader>.Instance);
        var handler = new GetOrdersQueryHandler(
            context, caller.Object, clock.Object, mapping.Object, cursor, sync,
            NullLogger<GetOrdersQueryHandler>.Instance);
        var query = new GetOrdersQuery(
            Status: null, PaymentStatus: null, OrderType: null,
            StartDate: null, EndDate: null, UserId: null,
            Search: null, IsFocusOrder: null, Scope: OrderListScope.All,
            SyncCursor: "AQ==");

        var act = () => handler.Handle(query, CancellationToken.None);

        (await act.Should().ThrowAsync<BadRequestException>())
            .Which.ErrorCode.Should().Be(ErrorCodes.InvalidOperationalQueueCursor);
    }

    [Fact]
    public void FilterHash_ChangesWhenAnyBoundFilterChanges()
    {
        var caller = new CursorUser(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var first = new GetOrdersQuery(
            Status: "Pending", PaymentStatus: null, OrderType: null,
            StartDate: null, EndDate: null, UserId: null,
            Search: null, IsFocusOrder: null, Scope: OrderListScope.Operational);
        var changed = first with { Status = "Confirmed" };
        var tenantRange = first with
        {
            TenantStartDay = new DateOnly(2026, 3, 28),
            TenantEndDay = new DateOnly(2026, 3, 30)
        };

        var firstHash = OperationalOrderQueryBuilder.FilterHash(first, caller);
        var changedHash = OperationalOrderQueryBuilder.FilterHash(changed, caller);
        var rangeHash = OperationalOrderQueryBuilder.FilterHash(tenantRange, caller);

        firstHash.Should().NotBe(changedHash);
        rangeHash.Should().NotBe(firstHash, "tenant-day range boundaries are cursor-bound filters");
    }

    private static OperationalQueueCursor CreateCursor(string tenant, TimeProvider? clock = null) =>
        CreateCursor(tenant, new EphemeralDataProtectionProvider(NullLoggerFactory.Instance), clock);

    private static OperationalQueueCursor CreateCursor(
        string tenant, IDataProtectionProvider provider, TimeProvider? clock = null) =>
        new(
            provider,
            Options.Create(new OperationalQueueSyncOptions
            {
                TenantKey = tenant,
                CursorLifetime = TimeSpan.FromMinutes(15)
            }),
            clock ?? TimeProvider.System);

    private static string ProtectJson(EphemeralDataProtectionProvider provider, object payload) =>
        provider.CreateProtector("RestaurantSystem.Orders.OperationalQueueSync.v1")
            .Protect(JsonSerializer.Serialize(payload));

    private sealed class FormatExceptionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new FormatExceptionProtector();
    }

    private sealed class FormatExceptionProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;

        public byte[] Protect(byte[] plaintext) => plaintext;

        public byte[] Unprotect(byte[] protectedData) => throw new FormatException("malformed");
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Current;
    }

    private sealed class CursorUser(Guid id) : ICurrentUserService
    {
        public Guid? UserId => id;
        public string? UserName => null;
        public string? Email => null;
        public UserRole? Role => UserRole.Cashier;
        public bool IsAuthenticated => true;
        public bool IsAdmin => false;
        public Task<ApplicationUser?> GetUserAsync() => Task.FromResult<ApplicationUser?>(null);
        public string GetAuditIdentifier() => id.ToString();
    }
}
