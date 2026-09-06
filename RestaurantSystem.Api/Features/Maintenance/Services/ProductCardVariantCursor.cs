using System.Globalization;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.Maintenance.Services;

internal sealed record ProductCardVariantCursor(DateTime CreatedAt, Guid Id)
{
    public static ProductCardVariantCursor? Parse(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var parts = value.Split(':');
        if (value.Length > 64 || parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks
            || !Guid.TryParseExact(parts[1], "N", out var id))
        {
            throw new BadRequestException("Invalid card-variant continuation cursor.");
        }

        return new ProductCardVariantCursor(new DateTime(ticks, DateTimeKind.Utc), id);
    }

    public override string ToString() =>
        $"{CreatedAt.Ticks.ToString(CultureInfo.InvariantCulture)}:{Id:N}";
}
