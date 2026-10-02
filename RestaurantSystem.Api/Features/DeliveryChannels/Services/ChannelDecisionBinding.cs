using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

internal static class ChannelDecisionBinding
{
    internal static IQueryable<ChannelOrderDecision> Eligible(IQueryable<ChannelOrderDecision> jobs,
        IOptions<DeliveryChannelSettings> options)
    {
        var result = jobs.Where(job => false);
        // Ambiguous deployment bindings remain paused instead of blocking unrelated stores.
        var unique = options.Value.Stores
            .GroupBy(store => (store.Provider, store.StoreId, store.Currency, store.IsSandbox))
            .Where(group => group.Count() == 1).Select(group => group.Single());
        foreach (var binding in unique.Where(store => !options.Value.SandboxOnly || store.IsSandbox))
        {
            result = result.Union(jobs.Where(job => job.Order.ExternalReference != null
                && job.Order.ExternalReference.Provider == binding.Provider
                && job.Order.ExternalReference.ExternalStoreId == binding.StoreId
                && job.Order.ExternalReference.Currency == binding.Currency
                && job.Order.ExternalReference.IsSandbox == binding.IsSandbox));
        }
        return result;
    }

    internal static void Require(ExternalOrderReference reference, IOptions<DeliveryChannelSettings> options)
        => Require(reference.Provider, reference.ExternalStoreId, reference.Currency, reference.IsSandbox, options);

    internal static void Require(string provider, string storeId, string currency, bool isSandbox, IOptions<DeliveryChannelSettings> options)
    {
        var settings = options.Value;
        var bindings = settings.Stores.Where(store => store.Provider == provider
            && store.StoreId == storeId && store.Currency == currency
            && store.IsSandbox == isSandbox).ToArray();
        if (!settings.Enabled || bindings.Length != 1 || settings.SandboxOnly && !isSandbox)
            throw new ForbiddenException("This marketplace store is not enabled for this tenant.");
    }

    internal static Dtos.ChannelDecisionDto Map(ChannelOrderDecision job)
        => new(job.OperationId, job.OrderId, job.Action, job.State, job.CreatedAt, job.LastObservedAt);
}
