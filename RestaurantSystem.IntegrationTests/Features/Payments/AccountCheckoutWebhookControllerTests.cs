using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Features.AccountPayments;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class AccountCheckoutWebhookControllerTests
{
    [Fact]
    public async Task Missing_secret_returns_503_without_reading_the_request_body()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new ThrowOnReadStream();
        var controller = Controller(context, new AccountCheckoutWebhookSettings());

        var result = await controller.Receive(CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task Oversized_payload_is_rejected_before_dispatch()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Headers["Stripe-Signature"] = "bounded";
        context.Request.Body = new MemoryStream(new byte[262145]);
        var controller = Controller(context, new AccountCheckoutWebhookSettings { SigningSecret = "whsec_test" });

        var result = await controller.Receive(CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task Oversized_signature_header_is_rejected_before_dispatch()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Headers["Stripe-Signature"] = new string('a', 1025);
        context.Request.Body = new ThrowOnReadStream();
        var controller = Controller(context, new AccountCheckoutWebhookSettings { SigningSecret = "whsec_test" });

        var result = await controller.Receive(CancellationToken.None);

        result.Should().BeOfType<BadRequestResult>();
        context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    private static AccountCheckoutWebhookController Controller(DefaultHttpContext context,
        AccountCheckoutWebhookSettings settings)
    {
        var controller = new AccountCheckoutWebhookController(
            new CustomMediator(new ServiceCollection().BuildServiceProvider()),
            Options.Create(settings));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private sealed class ThrowOnReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The request body should not be read.");
    }
}
