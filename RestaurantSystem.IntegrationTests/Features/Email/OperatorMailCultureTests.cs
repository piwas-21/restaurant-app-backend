using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Infrastructure.Settings;
using RestaurantSystem.IntegrationTests.Infrastructure;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Features.Email;

/// <summary>
/// GAP-2 S5 — the operator alert (M14) follows the TENANT, on a tenant whose language is not
/// English (EMAIL-LOCALISATION-PLAN §1 rank 4).
/// </summary>
/// <remarks>
/// A separate class because it needs a separate host: <c>Localization:DefaultLanguage</c> is bound
/// once per process. It exists because the same assertion on the default (unconfigured, English)
/// host passes for the wrong reason — English is also the placeholder S1 left behind, so
/// <c>Verify(SendOrderConfirmationAdminEmailAsync(English, …))</c> cannot fail on a revert. Here the
/// guest orders in French and the restaurant reads German, so one resolved culture shared by both
/// mails — the regression a restaurant notices first — fails loudly.
/// </remarks>
[Collection("Database Lane 2")]
public class OperatorMailCultureTests : IntegrationTestBase
{
    private const decimal PizzaPrice = 12.99m;
    private const string GuestEmail = "guest@example.com";

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    private readonly Mock<IEmailService> _email = new();
    private Guid _pizzaId;

    public OperatorMailCultureTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IEmailService>();
        services.AddSingleton(_email.Object);

        // A German restaurant that also sells in French — the tenant shape S9 will provision from
        // the registry, and the only one under which "operator" and "guest" are distinguishable.
        services.Configure<LocalizationSettings>(options =>
        {
            options.SupportedLanguages = "de,fr,en";
            options.DefaultLanguage = "de";
        });
    }

    [Fact]
    public async Task The_restaurant_reads_its_own_alert_in_its_own_language_while_the_guest_reads_theirs()
    {
        AuthenticateAsAnonymous();
        Client.DefaultRequestHeaders.Remove("Accept-Language");
        Client.DefaultRequestHeaders.Add("Accept-Language", "fr-CH,fr;q=0.9");

        var orderId = await PlaceOrderAsync();
        await WaitForMailsAsync(orderId);

        _email.Verify(e => e.SendOrderReceivedEmailAsync(
            French, GuestEmail, It.IsAny<string>(), It.IsAny<OrderMailDetails>()),
            Times.Once());

        _email.Verify(e => e.SendOrderConfirmationAdminEmailAsync(
            German, It.IsAny<string>(), It.IsAny<EmailGuest>(), It.IsAny<OrderMailDetails>()),
            Times.Once(),
            "the alert follows the tenant even when the diner asked for something else");
    }

    private async Task<Guid> PlaceOrderAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = OrderType.Takeaway.ToString(),
            customerName = "Guest",
            customerEmail = GuestEmail,
            items = new[] { new { productId = _pizzaId, quantity = 1, unitPrice = PizzaPrice } },
            payments = new[] { new { paymentMethod = PaymentMethod.Cash.ToString(), amount = PizzaPrice } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    private Task WaitForMailsAsync(Guid orderId) => WaitUntilAsync(async () =>
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var claims = await context.OutboundEmails.AsNoTracking()
            .Where(e => e.EntityId == orderId)
            .ToListAsync();

        return claims.Count(c => c.SentAt != null) == 2;
    });

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The awaited mail state never arrived.");
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _pizzaId = (await context.Products.AsNoTracking().FirstAsync(p => p.Name == "Test Pizza")).Id;
    }
}
