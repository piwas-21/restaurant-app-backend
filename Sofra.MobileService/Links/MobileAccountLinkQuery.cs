namespace Sofra.MobileService.Links;

public sealed record MobileAccountLinkQuery(string Slug, string Action, int QueryKeyCount, string RequestAuthority);
