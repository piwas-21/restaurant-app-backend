using System.Collections.ObjectModel;
using Microsoft.Extensions.Options;

namespace Sofra.MobileService.Links;

public sealed class GetMobileAssociationsQueryHandler : IGetMobileAssociationsQueryHandler
{
    private readonly bool _enabled;
    private readonly string _authority;
    private readonly AppleAppSiteAssociationDto? _apple;
    private readonly IReadOnlyList<AndroidAssetLinkDto>? _android;

    public GetMobileAssociationsQueryHandler(IOptions<MobileLinkSettings> links,
        IOptions<MobileAssociationSettings> associations)
    {
        var settings = associations.Value;
        _enabled = links.Value.Enabled && settings.Enabled;
        _authority = Uri.TryCreate(links.Value.Origin, UriKind.Absolute, out var origin) ? origin.Authority : string.Empty;
        if (!_enabled) return;
        var query = new ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["operation"] = "*" });
        var components = Array.AsReadOnly(new[] { new AppleLinkComponentDto("/r/*/payment-return/*", query) });
        var details = new AppleAppDetailsDto(Array.AsReadOnly(new[] { settings.AppleApplicationId }), components);
        _apple = new(new(Array.AsReadOnly(new[] { details })));
        var target = new AndroidAssetTargetDto("android_app", settings.AndroidPackage,
            Array.AsReadOnly(settings.AndroidSha256Fingerprints.ToArray()));
        _android = Array.AsReadOnly(new[] { new AndroidAssetLinkDto(
            Array.AsReadOnly(new[] { "delegate_permission/common.handle_all_urls" }), target) });
    }

    public AppleAppSiteAssociationDto? Apple(string requestAuthority) => Allowed(requestAuthority) ? _apple : null;
    public IReadOnlyList<AndroidAssetLinkDto>? Android(string requestAuthority) => Allowed(requestAuthority) ? _android : null;
    private bool Allowed(string authority) => _enabled && string.Equals(authority, _authority, StringComparison.OrdinalIgnoreCase);
}
