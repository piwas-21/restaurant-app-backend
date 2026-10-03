using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Settings;
using Stripe;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Authenticates account events and schedules a canonical provider read only.</summary>
public sealed class AccountCheckoutWebhookService(
    IOptions<AccountCheckoutWebhookSettings> options,
    IAccountStripeCheckoutClient provider,
    IAccountCheckoutLeaseStore leases) : IAccountCheckoutWebhookService
{

    public async Task<AccountCheckoutWebhookDisposition> HandleAsync(string payload, string? signature,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.SigningSecret))
            return AccountCheckoutWebhookDisposition.NotConfigured;
        if (string.IsNullOrEmpty(signature) || signature.Length > settings.MaximumSignatureHeaderLength)
            return AccountCheckoutWebhookDisposition.Invalid;

        Event stripeEvent;
        JsonDocument document;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signature, settings.SigningSecret,
                settings.SignatureToleranceSeconds, throwOnApiVersionMismatch: false);
            document = JsonDocument.Parse(payload);
        }
        catch (Exception exception) when (exception is StripeException or JsonException)
        {
            return AccountCheckoutWebhookDisposition.Invalid;
        }

        using (document)
        {
            AccountStripeContext expectedContext;
            try
            {
                expectedContext = provider.ReadContext();
            }
            catch (BadRequestException)
            {
                return AccountCheckoutWebhookDisposition.NotConfigured;
            }

            if (!string.Equals(stripeEvent.Account, expectedContext.ConnectedAccountId, StringComparison.Ordinal)
                || stripeEvent.Livemode != expectedContext.LiveMode)
                return AccountCheckoutWebhookDisposition.Invalid;

            if (!TryGetReferences(document.RootElement, stripeEvent.Type, out var references))
                return AccountCheckoutWebhookDisposition.Accepted;

            await leases.ScheduleWebhookWakeupAsync(references, expectedContext, cancellationToken);
            return AccountCheckoutWebhookDisposition.Accepted;
        }
    }

    private static bool TryGetReferences(JsonElement root, string? eventType,
        out AccountCheckoutWebhookReferences references)
    {
        references = new AccountCheckoutWebhookReferences(null, null, null, null);
        if (!IsRelevantEvent(eventType)
            || !root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("object", out var providerObject)
            || providerObject.ValueKind != JsonValueKind.Object)
            return false;

        var objectType = ReadString(providerObject, "object");
        if (objectType is null) return false;
        var metadataAttempt = ReadAttemptId(providerObject);
        string? sessionId = null;
        string? intentId = null;
        string? chargeId = null;

        switch (objectType)
        {
            case "checkout.session":
                sessionId = ReadString(providerObject, "id");
                intentId = ReadExpandableId(providerObject, "payment_intent");
                if (metadataAttempt is null && Guid.TryParse(ReadString(providerObject, "client_reference_id"), out var clientRef))
                    metadataAttempt = clientRef;
                break;
            case "payment_intent":
                intentId = ReadString(providerObject, "id");
                chargeId = ReadExpandableId(providerObject, "latest_charge");
                break;
            case "charge":
                chargeId = ReadString(providerObject, "id");
                intentId = ReadExpandableId(providerObject, "payment_intent");
                break;
            case "refund":
                intentId = ReadExpandableId(providerObject, "payment_intent");
                chargeId = ReadExpandableId(providerObject, "charge");
                break;
            case "dispute":
                chargeId = ReadExpandableId(providerObject, "charge");
                break;
            default:
                return false;
        }

        references = new AccountCheckoutWebhookReferences(metadataAttempt, sessionId, intentId, chargeId);
        return references.HasAny;
    }

    private static bool IsRelevantEvent(string? type) => type is not null
        && (type.StartsWith("checkout.session.", StringComparison.Ordinal)
            || type.StartsWith("payment_intent.", StringComparison.Ordinal)
            || type.StartsWith("charge.", StringComparison.Ordinal)
            || type.StartsWith("refund.", StringComparison.Ordinal));

    private static Guid? ReadAttemptId(JsonElement providerObject)
    {
        if (!providerObject.TryGetProperty("metadata", out var metadata)
            || metadata.ValueKind != JsonValueKind.Object
            || !string.Equals(ReadString(metadata, AccountStripeCheckoutClient.SchemaMetadataKey),
                AccountStripeCheckoutClient.SchemaVersion, StringComparison.Ordinal)
            || !Guid.TryParse(ReadString(metadata, AccountStripeCheckoutClient.AttemptMetadataKey), out var attemptId))
            return null;
        return attemptId;
    }

    private static string? ReadExpandableId(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var identifier)) return null;
        return identifier.ValueKind switch
        {
            JsonValueKind.String => identifier.GetString(),
            JsonValueKind.Object => ReadString(identifier, "id"),
            _ => null
        };
    }

    private static string? ReadString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}
