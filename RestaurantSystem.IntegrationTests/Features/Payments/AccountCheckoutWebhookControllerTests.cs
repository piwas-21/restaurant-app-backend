using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Features.AccountPayments;
using RestaurantSystem.Api.Features.AccountPayments.Commands.ProcessAccountCheckoutWebhookCommand;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class AccountCheckoutWebhookControllerTests
{
    private static readonly string TestSigningSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    [Fact]
    public async Task Missing_secret_returns_503_without_reading_the_request_body()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new ThrowOnReadStream();
        var controller = Controller(context, new AccountCheckoutWebhookSettings());

        var result = await controller.Receive([], CancellationToken.None);

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
        var controller = Controller(context, new AccountCheckoutWebhookSettings { SigningSecret = TestSigningSecret });

        var result = await controller.Receive(["bounded"], CancellationToken.None);

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
        var controller = Controller(context, new AccountCheckoutWebhookSettings { SigningSecret = TestSigningSecret });

        var result = await controller.Receive([new string('a', 1025)], CancellationToken.None);

        result.Should().BeOfType<BadRequestResult>();
        context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task Combined_signature_header_length_is_bounded_before_reading_the_body()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new ThrowOnReadStream();
        var controller = Controller(context, new AccountCheckoutWebhookSettings { SigningSecret = TestSigningSecret });

        var result = await controller.Receive([new string('a', 512), new string('b', 512)], CancellationToken.None);

        result.Should().BeOfType<BadRequestResult>();
        context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task Multiple_signature_headers_are_joined_in_order_for_the_webhook_service()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream("{}"u8.ToArray());
        var webhookService = new CapturingWebhookService();
        var services = new ServiceCollection();
        services.AddSingleton<IAccountCheckoutWebhookService>(webhookService);
        services.AddScoped<ICommandHandler<ProcessAccountCheckoutWebhookCommand, AccountCheckoutWebhookDisposition>,
            ProcessAccountCheckoutWebhookCommandHandler>();
        var controller = Controller(context, new AccountCheckoutWebhookSettings { SigningSecret = TestSigningSecret },
            services.BuildServiceProvider());

        var result = await controller.Receive(["t=timestamp", "v1=first", "v1=second"], CancellationToken.None);

        result.Should().BeOfType<OkResult>();
        webhookService.Signature.Should().Be("t=timestamp,v1=first,v1=second");
        webhookService.Payload.Should().Be("{}");
        var signatureParameter = typeof(AccountCheckoutWebhookController)
            .GetMethod(nameof(AccountCheckoutWebhookController.Receive))!
            .GetParameters()[0];
        signatureParameter.GetCustomAttributes(typeof(FromHeaderAttribute), inherit: false)
            .Cast<FromHeaderAttribute>().Single().Name.Should().Be("Stripe-Signature");
    }

    private static AccountCheckoutWebhookController Controller(DefaultHttpContext context,
        AccountCheckoutWebhookSettings settings, IServiceProvider? serviceProvider = null)
    {
        var controller = new AccountCheckoutWebhookController(
            new CustomMediator(serviceProvider ?? new ServiceCollection().BuildServiceProvider()),
            Options.Create(settings));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private sealed class CapturingWebhookService : IAccountCheckoutWebhookService
    {
        public string? Payload { get; private set; }
        public string? Signature { get; private set; }

        public Task<AccountCheckoutWebhookDisposition> HandleAsync(string payload, string? signature,
            CancellationToken cancellationToken)
        {
            Payload = payload;
            Signature = signature;
            return Task.FromResult(AccountCheckoutWebhookDisposition.Accepted);
        }
    }

    private sealed class ThrowOnReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The request body should not be read.");
    }
}
