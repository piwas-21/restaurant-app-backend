using Microsoft.Extensions.Options;
using Sofra.MobileService.Directory;

namespace Sofra.MobileService.Links;

/// <summary>Public navigation only: no customer credentials, tenant database or settlement service.</summary>
public sealed class GetMobilePaymentReturnQueryHandler(IOptions<MobileLinkSettings> settings,
    IGetMobileDirectoryQueryHandler directory) : IGetMobilePaymentReturnQueryHandler
{
    public bool Handle(MobilePaymentReturnQuery query) => MobileReturnNavigation.Allowed(settings.Value, directory,
        new(query.Slug, query.OrderReference, query.Operation, query.QueryKeyCount, query.OperationValueCount, query.RequestAuthority));
}
