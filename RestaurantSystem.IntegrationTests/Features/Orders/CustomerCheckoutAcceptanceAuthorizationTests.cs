using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutAcceptanceAuthorizationTests(DatabaseFixture fixture) : CustomerCheckoutAcceptanceFixture(fixture)
{
    private static readonly string[] Routes = ["prepare", "submit", "recover", "cancel"];
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        });
    }
    [Fact]
    public async Task All_native_acceptance_routes_refuse_staff_and_rejected_authentication_instead_of_guest_downgrade()
    {
        var submission = await PreparedAsync(await IntentAsync());
        foreach (var role in new[] { UserRole.Admin, UserRole.Server, UserRole.Cashier, UserRole.KitchenStaff })
        {
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                CustomerBasketTokens.Create(Factory.Services, role));
            foreach (var route in Routes)
                (await SendAsync(Path(route), Body(route, submission))).StatusCode.Should().Be(HttpStatusCode.Forbidden, route);
        }
        foreach (var token in new[] { "malformed-token", CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, expired: true) })
        {
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            foreach (var route in Routes)
                (await SendAsync(Path(route), Body(route, submission))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, route);
        }
        Client.DefaultRequestHeaders.Authorization = null;
        (await OrderResolutionAsync(await ResolveAsync(submission))).Outcome.Should().Be(CustomerOrderOperationOutcome.Unseen);
        await AssertNoFinancialEffectsAsync();
    }
    [Fact]
    public async Task All_native_acceptance_routes_require_the_installed_canonical_session()
    {
        var submission = await PreparedAsync(await IntentAsync());
        foreach (var session in new[] { "", "invalid", Guid.Empty.ToString("D"), "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA" })
            foreach (var route in Routes)
                (await SendAsync(Path(route), Body(route, submission), session)).StatusCode.Should().Be(HttpStatusCode.BadRequest, route);
        (await OrderResolutionAsync(await ResolveAsync(submission))).Outcome.Should().Be(CustomerOrderOperationOutcome.Unseen);
        await AssertNoFinancialEffectsAsync();
    }
    private static string Path(string route) => $"/api/customer-mobile/checkout/{route}";
    private static object Body(string route, CustomerCheckoutSubmissionDto submission) => route switch
    {
        "prepare" => submission.Intent,
        "submit" => submission,
        _ => new CustomerOrderRecoveryDto { SchemaVersion = 1, OperationId = submission.OperationId, PayloadHash = submission.PayloadHash }
    };
}
