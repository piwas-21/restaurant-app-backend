using Microsoft.Extensions.Options;
using Sofra.MobileService.Directory;

namespace Sofra.MobileService.Links;

public sealed class GetMobileTablePaymentReturnQueryHandler(IOptions<MobileLinkSettings> settings,
    IGetMobileDirectoryQueryHandler directory) : IGetMobileTablePaymentReturnQueryHandler
{
    public bool Handle(MobileTablePaymentReturnQuery query) => MobileReturnNavigation.Allowed(settings.Value, directory,
        query.Slug, query.AttemptReference, query.Operation, query.QueryKeyCount, query.OperationValueCount, query.RequestAuthority);
}
