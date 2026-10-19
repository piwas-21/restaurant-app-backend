using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderReceiptAuthorizationTests(DatabaseFixture fixture) : CustomerCheckoutAcceptanceFixture(fixture)
{
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
    public async Task Receipt_rejects_staff_failed_authentication_and_customer_attempts_to_borrow_a_guest_capability()
    {
        var submission = await PreparedAsync(await IntentAsync());
        await OrderResolutionAsync(await SubmitAsync(submission));
        var proof = new CustomerOrderRecoveryDto
        {
            SchemaVersion = 1,
            OperationId = submission.OperationId,
            PayloadHash = submission.PayloadHash
        };
        foreach (var role in new[] { UserRole.Admin, UserRole.Server, UserRole.Cashier, UserRole.KitchenStaff, UserRole.Customer })
        {
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CustomerBasketTokens.Create(Factory.Services, role));
            (await SendAsync("/api/customer-mobile/orders/receipt", proof)).StatusCode.Should().Be(
                role == UserRole.Customer ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden);
        }
        foreach (var token in new[] { "malformed-token", CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, expired: true) })
        {
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            (await SendAsync("/api/customer-mobile/orders/receipt", proof)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        Client.DefaultRequestHeaders.Authorization = null;
        foreach (var session in new[] { "", "invalid", Guid.Empty.ToString("D"), "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA" })
            (await SendAsync("/api/customer-mobile/orders/receipt", proof, session)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync("/api/customer-mobile/orders/receipt", new { proof.SchemaVersion, proof.OperationId, proof.PayloadHash, orderId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync("/api/customer-mobile/orders/receipt", proof)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
