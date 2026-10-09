using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class GuestRoundAvailabilityTests : IntegrationTestBase
{
    private static readonly TimeSpan ServiceOpen = new(11, 0, 0);
    private static readonly TimeSpan ServiceClose = new(23, 0, 0);
    private const string TenantTimeZone = "Europe/Zurich";
    private const string UnavailableMessage =
        "Dine-in ordering is currently unavailable. Your table visit and basket are still saved. Try again later or ask staff.";

    private readonly MutableClock _clock = new(
        new DateTimeOffset(2030, 5, 17, 4, 0, 0, TimeSpan.Zero), TenantTimeZone);
    private Guid _tableId;
    private Guid _productId;
    private string _qrCode = string.Empty;

    public GuestRoundAvailabilityTests(DatabaseFixture fixture) : base(fixture) { }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ITenantModules>();
        services.AddSingleton<ITenantModules>(new GuestRoundModules());
        services.RemoveAll<ITenantFeatures>();
        services.AddSingleton<ITenantFeatures>(new GuestRoundFeatures());
        services.RemoveAll<ITenantClock>();
        services.AddSingleton<ITenantClock>(_clock);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        _productId = await context.Products.Where(value => value.Name == "Test Pizza")
            .Select(value => value.Id).SingleAsync();
        _tableId = Guid.NewGuid();
        _qrCode = $"guest-availability-{Guid.NewGuid():N}";
        context.Tables.Add(new Table
        {
            Id = _tableId,
            TableNumber = "18",
            MaxGuests = 4,
            IsActive = true,
            ReadinessState = TableReadinessState.ReadyForGuests,
            QRCodeData = _qrCode,
            QRCodeGeneratedAt = DateTime.UtcNow,
            CreatedBy = nameof(GuestRoundAvailabilityTests),
        });

        await ConfigureDineInAsync(context, isEnabled: true, enforceOpeningHours: false);
        await ConfigureFridayHoursAsync(context);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Admission_code_defaults_to_legacy_length_and_shortens_only_when_requested()
    {
        AuthenticateAsRole(UserRole.Server);
        var sessionResponse = await PostAsJsonAsync("/api/table-service-sessions", new { tableId = _tableId });
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = (await ReadResponseAsync<ApiResponse<TableServiceSessionDto>>(sessionResponse))!.Data!;

        var legacyResponse = await PostAsJsonAsync(
            $"/api/table-guest-visits/{session.ServiceSessionId}/admission-code", new { });
        legacyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var legacyCode = (await ReadResponseAsync<ApiResponse<TableGuestAdmissionCodeDto>>(legacyResponse))!.Data!;
        legacyCode.AdmissionCode.Should().MatchRegex("^[0-9A-HJKMNP-TV-Z]{10}$");

        AuthenticateAsAnonymous();
        var legacyJoinResponse = await PostAsJsonAsync("/api/table-guest-visits/join", new
        {
            qrCodeData = _qrCode,
            admissionCode = legacyCode.AdmissionCode,
        });
        legacyJoinResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadResponseAsync<ApiResponse<TableGuestJoinDto>>(legacyJoinResponse))!.Success.Should().BeTrue();

        AuthenticateAsRole(UserRole.Server);
        var shortResponse = await PostAsJsonAsync(
            $"/api/table-guest-visits/{session.ServiceSessionId}/admission-code?preferShortCode=true", new { });
        shortResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var shortCode = (await ReadResponseAsync<ApiResponse<TableGuestAdmissionCodeDto>>(shortResponse))!.Data!;
        shortCode.AdmissionCode.Should().MatchRegex("^[0-9A-HJKMNP-TV-Z]{6}$");

        AuthenticateAsAnonymous();
        var shortJoinResponse = await PostAsJsonAsync("/api/table-guest-visits/join", new
        {
            qrCodeData = _qrCode,
            admissionCode = shortCode.AdmissionCode,
        });
        shortJoinResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadResponseAsync<ApiResponse<TableGuestJoinDto>>(shortJoinResponse))!.Success.Should().BeTrue();

        await using var verify = DatabaseFixture.CreateContext();
        var admissions = await verify.TableGuestAdmissions
            .Where(value => value.ServiceSessionId == session.ServiceSessionId)
            .ToListAsync();
        admissions.Should().HaveCount(2);
        admissions.Should().ContainSingle(value =>
            TableGuestCredentialCrypto.VerifyAdmissionCode(legacyCode.AdmissionCode, value.CodeHash)
            && value.RevokedAt.HasValue);
        admissions.Should().ContainSingle(value =>
            TableGuestCredentialCrypto.VerifyAdmissionCode(shortCode.AdmissionCode, value.CodeHash)
            && !value.RevokedAt.HasValue);
        admissions.All(value => !value.CodeHash.Contains(legacyCode.AdmissionCode)
            && !value.CodeHash.Contains(shortCode.AdmissionCode)).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, false, true)]
    public async Task Guest_round_submission_obeys_DineIn_availability(
        bool isEnabled, bool enforceOpeningHours, bool duringService, bool accepted)
    {
        var guest = await PrepareGuestBasketAsync();
        _clock.Set(duringService ? ServiceInstant() : ClosedInstant(), TenantTimeZone);
        await using (var context = DatabaseFixture.CreateContext())
        {
            await ConfigureDineInAsync(context, isEnabled, enforceOpeningHours);
            await context.SaveChangesAsync();
        }

        var sessionBefore = await ReadSessionStateAsync(guest.ServiceSessionId);
        var roundRequest = new
        {
            operationId = Guid.NewGuid(),
            expectedAccountRevision = guest.AccountRevision,
            expectedBasketFingerprint = guest.PurchaseFingerprint,
        };
        var response = await Client.PostAsJsonAsync(
            $"/api/table-guest-visits/{guest.ServiceSessionId}/rounds", roundRequest);

        if (accepted)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = (await ReadResponseAsync<ApiResponse<TableGuestAccountDto>>(response))!;
            result.Success.Should().BeTrue();
            result.Data!.AccountRevision.Should().Be(guest.AccountRevision + 1);
            result.Data.Orders.Should().ContainSingle();

            await using var verify = DatabaseFixture.CreateContext();
            (await verify.Orders.CountAsync(value => value.ServiceSessionId == guest.ServiceSessionId))
                .Should().Be(1);
            (await verify.TableGuestRoundOperations.CountAsync(value => value.ServiceSessionId == guest.ServiceSessionId))
                .Should().Be(1);
            (await verify.OrderPayments.AnyAsync(value => value.Order.ServiceSessionId == guest.ServiceSessionId))
                .Should().BeFalse("guest rounds must not create payments");
            (await verify.AccountPaymentAttempts.AnyAsync(value => value.ServiceSessionId == guest.ServiceSessionId))
                .Should().BeFalse("guest rounds must not create account payment attempts");
            (await ReadSessionStateAsync(guest.ServiceSessionId)).AccountRevision.Should()
                .Be(guest.AccountRevision + 1);

            _clock.Set(ClosedInstant(), TenantTimeZone);
            await using (var context = DatabaseFixture.CreateContext())
            {
                await ConfigureDineInAsync(context, isEnabled: false, enforceOpeningHours: true);
                await context.SaveChangesAsync();
            }

            var replayResponse = await Client.PostAsJsonAsync(
                $"/api/table-guest-visits/{guest.ServiceSessionId}/rounds", roundRequest);
            replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var replay = (await ReadResponseAsync<ApiResponse<TableGuestAccountDto>>(replayResponse))!;
            replay.Success.Should().BeTrue(replay.Message);
            replay.Data!.AccountRevision.Should().Be(guest.AccountRevision + 1);
            replay.Data.Orders.Should().ContainSingle().Which.OrderId.Should()
                .Be(result.Data.Orders.Single().OrderId);

            await using var replayVerify = DatabaseFixture.CreateContext();
            (await replayVerify.Orders.CountAsync(value => value.ServiceSessionId == guest.ServiceSessionId))
                .Should().Be(1);
            (await replayVerify.TableGuestRoundOperations.CountAsync(value =>
                value.ServiceSessionId == guest.ServiceSessionId)).Should().Be(1);
            (await replayVerify.OrderPayments.AnyAsync(value =>
                value.Order.ServiceSessionId == guest.ServiceSessionId)).Should().BeFalse();
            (await replayVerify.AccountPaymentAttempts.AnyAsync(value =>
                value.ServiceSessionId == guest.ServiceSessionId)).Should().BeFalse();
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var refusal = (await ReadResponseAsync<ApiResponse<object>>(response))!;
        refusal.Success.Should().BeFalse();
        refusal.ErrorCode.Should().Be(ErrorCodes.OrderTypeNotAvailable);
        refusal.Message.Should().Be(UnavailableMessage);

        await AssertRefusalDidNotChangeVisitOrBasketAsync(guest, sessionBefore);
    }

    private async Task<GuestBasket> PrepareGuestBasketAsync()
    {
        AuthenticateAsRole(UserRole.Server);
        var sessionResponse = await PostAsJsonAsync("/api/table-service-sessions", new { tableId = _tableId });
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionEnvelope = (await ReadResponseAsync<ApiResponse<TableServiceSessionDto>>(sessionResponse))!;
        sessionEnvelope.Success.Should().BeTrue(sessionEnvelope.Message);
        sessionEnvelope.Data.Should().NotBeNull(sessionEnvelope.Message);
        var session = sessionEnvelope.Data!;

        var admissionResponse = await PostAsJsonAsync(
            $"/api/table-guest-visits/{session.ServiceSessionId}/admission-code", new { });
        admissionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var admission = (await ReadResponseAsync<ApiResponse<TableGuestAdmissionCodeDto>>(admissionResponse))!.Data!;

        AuthenticateAsAnonymous();
        var joinResponse = await PostAsJsonAsync("/api/table-guest-visits/join", new
        {
            qrCodeData = _qrCode,
            admissionCode = admission.AdmissionCode,
        });
        joinResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var participant = (await ReadResponseAsync<ApiResponse<TableGuestJoinDto>>(joinResponse))!.Data!;
        var basketSession = Guid.NewGuid().ToString("N");
        Client.DefaultRequestHeaders.Add("X-Session-Id", basketSession);
        Client.DefaultRequestHeaders.Add("X-Table-Participant", participant.ParticipantToken);

        (await Client.PutAsJsonAsync("/api/Basket/order-type", new { orderType = "DineIn" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsJsonAsync("/api/Basket/items", new AddToBasketDto
        {
            ProductId = _productId,
            Quantity = 1,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var basketResponse = await Client.GetAsync("/api/Basket");
        basketResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = (await ReadResponseAsync<ApiResponse<BasketDto>>(basketResponse))!.Data!;
        var accountResponse = await Client.GetAsync($"/api/table-guest-visits/{session.ServiceSessionId}/account");
        accountResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var account = (await ReadResponseAsync<ApiResponse<TableGuestAccountDto>>(accountResponse))!.Data!;
        account.AccountRevision.Should().Be(1);

        return new GuestBasket(
            session.ServiceSessionId, basketSession, basket.PurchaseFingerprint, account.AccountRevision);
    }

    private async Task AssertRefusalDidNotChangeVisitOrBasketAsync(
        GuestBasket guest, SessionState sessionBefore)
    {
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync(value => value.ServiceSessionId == guest.ServiceSessionId)).Should().Be(0);
        (await context.OrderPayments.AnyAsync(value => value.Order.ServiceSessionId == guest.ServiceSessionId))
            .Should().BeFalse("refused guest rounds must not create payments");
        (await context.AccountPaymentAttempts.AnyAsync(value => value.ServiceSessionId == guest.ServiceSessionId))
            .Should().BeFalse("refused guest rounds must not create account payment attempts");
        (await context.TableGuestRoundOperations.CountAsync(value => value.ServiceSessionId == guest.ServiceSessionId))
            .Should().Be(0);
        (await context.TableGuestParticipants.CountAsync(value =>
            value.ServiceSessionId == guest.ServiceSessionId && value.RevokedAt == null)).Should().Be(1);
        var draft = await context.Baskets.AsNoTracking()
            .Where(value => value.SessionId == guest.BasketSessionId)
            .Select(value => new { value.OrderType, value.SubTotal, value.Total, ItemCount = value.Items.Count })
            .SingleAsync();
        draft.OrderType.Should().Be(OrderType.DineIn);
        draft.ItemCount.Should().Be(1);

        var sessionAfter = await ReadSessionStateAsync(guest.ServiceSessionId);
        sessionAfter.Should().BeEquivalentTo(sessionBefore);

        var basketResponse = await Client.GetAsync("/api/Basket");
        basketResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = (await ReadResponseAsync<ApiResponse<BasketDto>>(basketResponse))!.Data!;
        basket.OrderType.Should().Be(OrderType.DineIn);
        basket.PurchaseFingerprint.Should().Be(guest.PurchaseFingerprint);
        basket.Items.Should().ContainSingle();

        var accountResponse = await Client.GetAsync($"/api/table-guest-visits/{guest.ServiceSessionId}/account");
        accountResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var account = (await ReadResponseAsync<ApiResponse<TableGuestAccountDto>>(accountResponse))!.Data!;
        account.AccountRevision.Should().Be(guest.AccountRevision);
        account.Orders.Should().BeEmpty();
    }

    private async Task<SessionState> ReadSessionStateAsync(Guid serviceSessionId)
    {
        await using var context = DatabaseFixture.CreateContext();
        return await context.TableServiceSessions.AsNoTracking()
            .Where(value => value.Id == serviceSessionId)
            .Select(value => new SessionState(value.Status, value.Version, value.AccountRevision, value.Currency))
            .SingleAsync();
    }

    private static async Task ConfigureDineInAsync(
        ApplicationDbContext context, bool isEnabled, bool enforceOpeningHours)
    {
        var dineIn = await context.OrderTypeConfigurations
            .SingleOrDefaultAsync(value => value.OrderType == OrderType.DineIn);
        if (dineIn is null)
        {
            dineIn = new OrderTypeConfiguration
            {
                OrderType = OrderType.DineIn,
                DisplayOrder = (int)OrderType.DineIn,
                CreatedBy = nameof(GuestRoundAvailabilityTests),
            };
            context.OrderTypeConfigurations.Add(dineIn);
        }

        dineIn.IsEnabled = isEnabled;
        dineIn.EnforceOpeningHours = enforceOpeningHours;
    }

    private static async Task ConfigureFridayHoursAsync(ApplicationDbContext context)
    {
        var friday = await context.WorkingHours.Include(value => value.Shifts)
            .SingleOrDefaultAsync(value => value.DayOfWeek == DayOfWeek.Friday);
        if (friday is null)
        {
            friday = new WorkingHours
            {
                DayOfWeek = DayOfWeek.Friday,
                OpenTime = ServiceOpen,
                CloseTime = ServiceClose,
                CreatedBy = nameof(GuestRoundAvailabilityTests),
            };
            context.WorkingHours.Add(friday);
        }

        context.WorkingHoursShifts.RemoveRange(friday.Shifts);
        friday.Shifts.Clear();
        friday.OpenTime = ServiceOpen;
        friday.CloseTime = ServiceClose;
        friday.IsActive = true;
        friday.IsClosed = false;
        friday.Shifts.Add(new WorkingHoursShift
        {
            WorkingHoursId = friday.Id,
            OpenTime = ServiceOpen,
            CloseTime = ServiceClose,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(GuestRoundAvailabilityTests),
        });
    }

    private static DateTimeOffset ClosedInstant() => new(2030, 5, 17, 4, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset ServiceInstant() => new(2030, 5, 17, 10, 0, 0, TimeSpan.Zero);

    private sealed record GuestBasket(
        Guid ServiceSessionId, string BasketSessionId, string PurchaseFingerprint, long AccountRevision);

    private sealed record SessionState(
        TableServiceSessionStatus Status, int Version, long AccountRevision, string? Currency);

    private sealed class GuestRoundModules : ITenantModules
    {
        private static readonly string[] Enabled =
            [ModuleIds.Core, ModuleIds.KitchenBoard, ModuleIds.Server, ModuleIds.Cashier, ModuleIds.Printing];

        public bool IsEnforced => true;
        public IReadOnlyList<string> EnabledModules => Enabled;
        public bool IsEnabled(string moduleId) => Enabled.Contains(moduleId, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class GuestRoundFeatures : ITenantFeatures
    {
        public bool ServerWorkspaceV2 => true;
        public bool TableAccountV1 => true;
        public bool OrderAmendmentsV1 => true;
        public bool TableGuestVisitsV1 => true;
        public bool TableVisitReadinessV1 => true;
        public bool TableAccountPaymentsV1 => false;
        public bool ServerAccountCollectionV1 => false;
        public bool TableGuestAccountPaymentsV1 => false;
        public bool EnforceSauceMinimum => false;
        public bool OptionSetMaterializationEnabled => false;
    }
}
