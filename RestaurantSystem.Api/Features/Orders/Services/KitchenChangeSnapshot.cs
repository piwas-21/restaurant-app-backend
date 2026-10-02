using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Stores the reviewed preparation payload independently of mutable order rows.</summary>
internal static class KitchenChangeSnapshot
{
    internal static string Serialize(IReadOnlyList<PrinterFeedChangeDto> changes)
    {
        if (changes.Count == 0)
        {
            throw new BadRequestException("A kitchen change job must contain a preparation change.");
        }

        foreach (var change in changes)
        {
            var validShape = change.Kind switch
            {
                KitchenChangeKind.Add => change.Previous is null && change.Current is not null,
                KitchenChangeKind.Void => change.Previous is not null && change.Current is null,
                KitchenChangeKind.Replace => change.Previous is not null && change.Current is not null,
                KitchenChangeKind.InstructionChange => change.Previous is not null
                    && change.Current is not null && change.Previous.Id == change.Current.Id
                    && change.Previous.Quantity == change.Current.Quantity,
                _ => false
            };
            if (!validShape)
            {
                throw new BadRequestException("The kitchen change does not match its preparation action.");
            }

            ValidateItem(change.Previous);
            ValidateItem(change.Current);
        }

        return JsonSerializer.Serialize(changes);
    }

    internal static IReadOnlyList<PrinterFeedChangeDto> Deserialize(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<List<PrinterFeedChangeDto>>(json)
            ?? throw new JsonException("The persisted kitchen change snapshot is missing.");

    private static void ValidateItem(OrderItemDto? item)
    {
        if (item is null) return;
        if (item.Id == Guid.Empty || item.Quantity <= 0 || string.IsNullOrWhiteSpace(item.ProductName))
        {
            throw new BadRequestException("A kitchen change requires an identified item and positive quantity.");
        }

        foreach (var child in item.SideItems ?? [])
        {
            ValidateItem(child);
        }
    }
}
