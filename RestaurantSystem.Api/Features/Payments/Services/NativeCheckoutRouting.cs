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
            || !ValidSlug(slug)
            || reference != orderId.ToString("D") || uri.Query != $"?operation={OperationId:D}"
            || ReturnUrl != $"{uri.GetLeftPart(UriPartial.Authority)}/r/{slug}/payment-return/{orderId:D}?operation={OperationId:D}")
            throw new BadRequestException("The frozen native payment return URL is invalid.");
    }

    public static bool IsValidConfiguration(string linkOrigin, string tenantSlug) => ValidSlug(tenantSlug)
        && Uri.TryCreate(linkOrigin, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && !linkOrigin.Contains('\\') && linkOrigin.Length <= 1024
        && linkOrigin == uri.GetLeftPart(UriPartial.Authority);

    public static NativeCheckoutRouting Create(string linkOrigin, string tenantSlug, Guid orderId,
        Guid operationId, StripeProviderContext context)
    {
        if (!IsValidConfiguration(linkOrigin, tenantSlug))
            throw new BadRequestException("Native payment links require reviewed configuration.");
        var routing = new NativeCheckoutRouting(operationId, context,
            $"{linkOrigin}/r/{tenantSlug}/payment-return/{orderId:D}?operation={operationId:D}");
        routing.RequireReturn(orderId);
        return routing;
    }

    private static bool ValidSlug(string slug) => slug.Length is >= 1 and <= 64
        && slug.Split('-').All(part => part.Length > 0)
        && slug.All(value => char.IsAsciiLetterLower(value) || char.IsAsciiDigit(value) || value == '-');

    public Dictionary<string, string> Metadata() => new()
    {
        [OperationMetadataKey] = OperationId.ToString("D"),
        [SchemaMetadataKey] = SchemaVersion
    };
}
