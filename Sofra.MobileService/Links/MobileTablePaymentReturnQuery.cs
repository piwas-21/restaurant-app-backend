namespace Sofra.MobileService.Links;

public sealed record MobileTablePaymentReturnQuery(string Slug, string AttemptReference, string? Operation,
    int QueryKeyCount, int OperationValueCount, string RequestAuthority);
