namespace Sofra.MobileService.Links;

public sealed record MobilePaymentReturnQuery(string Slug, string OrderReference, string? Operation,
    int QueryKeyCount, int OperationValueCount, string RequestAuthority);
