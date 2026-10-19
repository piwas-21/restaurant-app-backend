using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <summary>Server-frozen routing for one native order; identifiers grant no access.</summary>
public sealed record NativeCheckoutRouting(Guid OperationId, StripeProviderContext Context,
    string ReturnUrl)
{
    public const string OperationMetadataKey = "native_order_operation";
    public const string SchemaMetadataKey = "sofra_payment_schema";
    public const string SchemaVersion = "native-order-v1";

    public void RequireCurrent(IStripeGateway gateway, StripeSettings settings, Guid orderId)
    {
        if (Context != StripeProviderContext.Read(gateway, settings))
            throw new ConflictException("Reconcile the original payment account and environment.");
        RequireReturn(orderId);
    }

    public void RequireReturn(Guid orderId)
    {
        if (OperationId == Guid.Empty || orderId == Guid.Empty
            || !Uri.TryCreate(ReturnUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || ReturnUrl.Contains('\\') || ReturnUrl.Length > 2048
            || uri.AbsolutePath.Split('/') is not ["", "r", var slug, "payment-return", var reference]
            || slug.Length is < 1 or > 64 || slug.Split('-').Any(part => part.Length == 0)
            || slug.Any(value => !char.IsAsciiLetterLower(value) && !char.IsAsciiDigit(value) && value != '-')
            || reference != orderId.ToString("D") || uri.Query != $"?operation={OperationId:D}"
            || ReturnUrl != $"{uri.GetLeftPart(UriPartial.Authority)}/r/{slug}/payment-return/{orderId:D}?operation={OperationId:D}")
            throw new BadRequestException("The frozen native payment return URL is invalid.");
    }

    public Dictionary<string, string> Metadata() => new()
    {
        [OperationMetadataKey] = OperationId.ToString("D"),
        [SchemaMetadataKey] = SchemaVersion
    };
}
