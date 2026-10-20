namespace Sofra.MobileService.Links;

public interface IGetMobileAssociationsQueryHandler
{
    AppleAppSiteAssociationDto? Apple(string requestAuthority);
    IReadOnlyList<AndroidAssetLinkDto>? Android(string requestAuthority);
}
