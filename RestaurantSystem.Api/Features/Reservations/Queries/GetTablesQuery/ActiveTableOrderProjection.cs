using System.Globalization;
using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetTablesQuery;

internal sealed record TableOrderKey(Guid? TableId, int? TableNumber);

internal sealed class ActiveTableOrderProjection(
    Dictionary<TableOrderKey, ActiveTableOrderInfo> byIdentity)
{
    public static ActiveTableOrderProjection Empty { get; } =
        new(new Dictionary<TableOrderKey, ActiveTableOrderInfo>());

    public bool TryGet(TableDto table, out ActiveTableOrderInfo info)
    {
        byIdentity.TryGetValue(new TableOrderKey(table.Id, null), out var stable);
        ActiveTableOrderInfo? legacy = null;
        var hasLegacy = TryReadCanonicalNumber(table.TableNumber, out var number)
            && byIdentity.TryGetValue(new TableOrderKey(null, number), out legacy);
        if (stable is not null && hasLegacy)
        {
            info = new ActiveTableOrderInfo
            {
                OrderCount = stable.OrderCount + legacy!.OrderCount,
                Occupants = stable.Occupants.Concat(legacy.Occupants)
                    .OrderByDescending(occupant => occupant.OrderDate)
                    .Take(1)
                    .ToList()
            };
            return true;
        }

        info = stable ?? (hasLegacy ? legacy! : null!);
        return info is not null;
    }

    private static bool TryReadCanonicalNumber(string label, out int number) =>
        int.TryParse(label, NumberStyles.None, CultureInfo.InvariantCulture, out number)
        && number > 0
        && number.ToString(CultureInfo.InvariantCulture) == label;
}

internal sealed class ActiveTableOrderInfo
{
    public int OrderCount { get; init; }
    public List<TableOccupantDto> Occupants { get; init; } = [];
}
