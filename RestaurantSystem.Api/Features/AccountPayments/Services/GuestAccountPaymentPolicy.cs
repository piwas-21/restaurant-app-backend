using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Payments.Interfaces;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IGuestAccountPaymentPolicy
{
    void RequireAccountRead();
    void RequireNewPayment();
    void RequireContribution(long amountMinor, string currency);
    AccountOnlineContributionLimitsDto? ReadLimits(string currency);
}

public sealed class GuestAccountPaymentPolicy(
    ITenantFeatures features,
    ITenantModules modules,
    IStripeGateway gateway,
    IOptions<AccountOnlineContributionSettings> limits) : IGuestAccountPaymentPolicy
{
    public void RequireAccountRead()
    {
        if (!features.TableGuestVisitsV1 || !features.TableAccountPaymentsV1)
            throw Unavailable();
    }

    public void RequireNewPayment()
    {
        if (!features.TableGuestVisitsV1 || !features.TableAccountPaymentsV1
            || !features.TableGuestAccountPaymentsV1
            || !modules.IsEnabled(ModuleIds.OnlinePayments)
            || !gateway.IsConfigured || string.IsNullOrWhiteSpace(limits.Value.SettlementCurrency)
            || !limits.Value.HasValidLimits())
        {
            throw Unavailable();
        }
    }

    public void RequireContribution(long amountMinor, string currency)
    {
        RequireNewPayment();
        var configured = ReadLimits(currency) ?? throw Unavailable();
        if (amountMinor < configured.MinimumAmountMinor || amountMinor > configured.MaximumAmountMinor)
            throw new BadRequestException("Choose a contribution within the online payment limits, or ask staff to collect it.");
    }

    public AccountOnlineContributionLimitsDto? ReadLimits(string currency)
    {
        var configured = limits.Value;
        if (!features.TableGuestVisitsV1 || !features.TableAccountPaymentsV1
            || !features.TableGuestAccountPaymentsV1 || !modules.IsEnabled(ModuleIds.OnlinePayments)
            || !gateway.IsConfigured || currency != configured.SettlementCurrency || !configured.HasValidLimits())
            return null;
        return new(currency, configured.MinimumAmountMinor, configured.MaximumAmountMinor);
    }

    private static NotFoundException Unavailable() =>
        new("Guest online account payments are unavailable for this table visit.");
}
