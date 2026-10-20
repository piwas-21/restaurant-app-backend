using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.Api.Features.Tenant.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;

public sealed record GetCustomerMobileBootstrapQuery(int ContractVersion)
    : IQuery<ApiResponse<CustomerMobileBootstrapDto>>;

public sealed class GetCustomerMobileBootstrapQueryHandler(
    ApplicationDbContext context,
    IOptions<CustomerMobileSettings> mobile,
    IOptions<CustomerNativeAccountSettings> nativeAccounts,
    IOptions<LocalizationSettings> localization,
    ITenantModules modules,
    ITenantFeatures features,
    ITenantClock clock)
    : IQueryHandler<GetCustomerMobileBootstrapQuery, ApiResponse<CustomerMobileBootstrapDto>>
{
    public async Task<ApiResponse<CustomerMobileBootstrapDto>> Handle(
        GetCustomerMobileBootstrapQuery query, CancellationToken cancellationToken)
    {
        var settings = mobile.Value;
        if (!settings.Enabled || !settings.IsValid())
            throw new NotFoundException("Customer mobile access is unavailable.");

        var currency = await context.RestaurantInfo.AsNoTracking()
            .Select(info => info.Currency).FirstOrDefaultAsync(cancellationToken);
        var supported = query.ContractVersion == 1;
        var capabilities = MobileCapabilityPolicy.Resolve(settings, modules, features,
            !string.IsNullOrWhiteSpace(currency), supported);
        var dto = new CustomerMobileBootstrapDto(1, settings.TenantId, currency,
            localization.Value.DefaultLanguage, clock.TimeZone.Id, settings.Lifecycle,
            supported ? "supported" : "unavailable", modules.EnabledModules, modules.IsEnforced,
            new Dictionary<string, bool>
            {
                ["nativeAccountLinksV1"] = nativeAccounts.Value.Enabled && nativeAccounts.Value.IsValid()
                    && capabilities.GetValueOrDefault("accounts") == "enabled",
                ["tableGuestVisitsV1"] = features.TableGuestVisitsV1,
                ["tableGuestAccountPaymentsV1"] = features.TableGuestAccountPaymentsV1,
                ["tableAccountPaymentsV1"] = features.TableAccountPaymentsV1
            },
            capabilities);
        return ApiResponse<CustomerMobileBootstrapDto>.SuccessWithData(dto);
    }
}
