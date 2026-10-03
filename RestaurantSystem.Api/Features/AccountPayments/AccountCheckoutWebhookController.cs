using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Features.AccountPayments.Commands.ProcessAccountCheckoutWebhookCommand;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.AccountPayments;

[ApiController]
[Route("api/webhooks/stripe/account-checkouts")]
public sealed class AccountCheckoutWebhookController(CustomMediator mediator,
    IOptions<AccountCheckoutWebhookSettings> options) : ControllerBase
{
    private const int ReadBufferBytes = 8192;

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(AccountCheckoutWebhookServiceExtensions.PolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Receive(
        [FromHeader(Name = "Stripe-Signature")] string[]? signatureHeaders,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.SigningSecret))
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        if (!Request.HasJsonContentType()) return StatusCode(StatusCodes.Status415UnsupportedMediaType);
        var signature = JoinBoundedSignatureHeaders(signatureHeaders, settings.MaximumSignatureHeaderLength);
        if (signature is null) return BadRequest();
        if (Request.ContentLength > settings.MaximumPayloadBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        var payload = await ReadBoundedPayloadAsync(Request.Body, settings.MaximumPayloadBytes, cancellationToken);
        if (payload is null) return StatusCode(StatusCodes.Status413PayloadTooLarge);

        var disposition = await mediator.SendCommand(
            new ProcessAccountCheckoutWebhookCommand(payload, signature), cancellationToken);
        return disposition switch
        {
            AccountCheckoutWebhookDisposition.NotConfigured => StatusCode(StatusCodes.Status503ServiceUnavailable),
            AccountCheckoutWebhookDisposition.Invalid => BadRequest(),
            _ => Ok()
        };
    }

    private static string? JoinBoundedSignatureHeaders(string[]? signatureHeaders, int maximumLength)
    {
        if (signatureHeaders is null || signatureHeaders.Length == 0) return string.Empty;
        var combinedLength = signatureHeaders.Length - 1;
        if (combinedLength > maximumLength) return null;
        foreach (var headerLength in signatureHeaders.Select(header => header.Length))
        {
            if (headerLength > maximumLength - combinedLength) return null;
            combinedLength += headerLength;
        }
        return string.Join(',', signatureHeaders);
    }

    private static async Task<string?> ReadBoundedPayloadAsync(Stream body, int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream();
        var buffer = new byte[ReadBufferBytes];
        while (true)
        {
            var read = await body.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (payload.Length + read > maximumBytes) return null;
            await payload.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return Encoding.UTF8.GetString(payload.ToArray());
    }
}
