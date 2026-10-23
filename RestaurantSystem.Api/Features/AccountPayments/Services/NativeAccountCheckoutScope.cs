using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Frozen native table identity; never an order reference or a navigation authority.</summary>
public sealed record NativeAccountCheckoutScope(Guid TenantId, Guid OperationId)
{
    private const int MaximumReturnBaseLength = 2048;
    public const string TenantMetadataKey = "native_table_tenant";
    public const string OperationMetadataKey = "native_table_operation";
    public const string SchemaVersion = "native-table-payment-v1";

    public void RequireValid()
    {
        if (TenantId == Guid.Empty || OperationId == Guid.Empty)
            throw new ConflictException("The original native contribution identity requires reconciliation.");
    }

    public void RequireReturnBase(string value, Guid attemptId)
    {
        RequireValid();
        if (attemptId == Guid.Empty || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || value.Length > MaximumReturnBaseLength || value.Contains('\\')
            || uri.AbsolutePath.Split('/') is not ["", "r", var slug, "table-payment-return", var reference]
            || reference != attemptId.ToString("D")
            || !NativeCheckoutRouting.IsValidConfiguration(uri.GetLeftPart(UriPartial.Authority), slug)
            || value != $"{uri.GetLeftPart(UriPartial.Authority)}/r/{slug}/table-payment-return/{attemptId:D}")
            throw new BadRequestException("The frozen native table-payment return path is invalid.");
    }

    public string ReturnUrl(string returnBase, Guid attemptId)
    {
        RequireReturnBase(returnBase, attemptId);
        return $"{returnBase}?operation={OperationId:D}";
    }

    public string ReturnBase(string origin, string slug, Guid attemptId)
    {
        if (!NativeCheckoutRouting.IsValidConfiguration(origin, slug))
            throw new BadRequestException("Native table-payment links require reviewed configuration.");
        var value = $"{origin}/r/{slug}/table-payment-return/{attemptId:D}";
        RequireReturnBase(value, attemptId);
        return value;
    }

    public Dictionary<string, string> Metadata(Guid attemptId)
    {
        RequireValid();
        return new()
        {
            [AccountStripeCheckoutClient.AttemptMetadataKey] = attemptId.ToString("D"),
            [AccountStripeCheckoutClient.SchemaMetadataKey] = SchemaVersion,
            [TenantMetadataKey] = TenantId.ToString("D"),
            [OperationMetadataKey] = OperationId.ToString("D")
        };
    }
}
