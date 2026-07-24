using System.Text.Json;
using RestaurantSystem.Api.Features.FloorPlan.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FloorPlan.Services;

/// <summary>
/// Entity ↔ DTO mapping for the floor-plan document, plus the coordinate clamps
/// that keep saved geometry inside the plan bounds (FLOOR-PLAN-REVAMP §5.1). Kept
/// separate from <see cref="FloorPlanService"/> so each stays within its file
/// limit and the arithmetic is unit-testable in isolation.
/// </summary>
public static class FloorPlanDocumentMapper
{
    private static readonly JsonSerializerOptions PointsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static FloorPlanDocumentDto ToDocumentDto(Domain.Entities.FloorPlan plan, IReadOnlyList<Table> tables) => new()
    {
        Id = plan.Id,
        Name = plan.Name,
        WidthMeters = plan.WidthMeters,
        HeightMeters = plan.HeightMeters,
        GridSizeCm = plan.GridSizeCm,
        BackgroundStyle = plan.BackgroundStyle,
        IsDefault = plan.IsDefault,
        DisplayOrder = plan.DisplayOrder,
        UpdatedAt = plan.UpdatedAt,
        Walls = plan.Walls.OrderBy(w => w.ZIndex).Select(ToWallDto).ToList(),
        Items = plan.Items.OrderBy(i => i.ZIndex).Select(ToItemDto).ToList(),
        Tables = tables.Select(ToTableDto).ToList(),
    };

    private static FloorPlanWallDto ToWallDto(FloorPlanWall wall) => new()
    {
        Id = wall.Id,
        Points = DeserializePoints(wall.PointsJson),
        ThicknessMeters = wall.ThicknessMeters,
        IsClosed = wall.IsClosed,
        RoomName = wall.RoomName,
        FloorStyle = wall.FloorStyle,
        ZIndex = wall.ZIndex,
        Openings = wall.Openings.Select(o => new FloorPlanOpeningDto
        {
            Id = o.Id,
            SegmentIndex = o.SegmentIndex,
            OffsetMeters = o.OffsetMeters,
            WidthMeters = o.WidthMeters,
            Kind = o.Kind,
            SwingDirection = o.SwingDirection,
        }).ToList(),
    };

    private static FloorPlanItemDto ToItemDto(FloorPlanItem item) => new()
    {
        Id = item.Id,
        Kind = item.Kind,
        X = item.X,
        Y = item.Y,
        WidthMeters = item.WidthMeters,
        HeightMeters = item.HeightMeters,
        RotationDegrees = item.RotationDegrees,
        ZIndex = item.ZIndex,
        Label = item.Label,
        StyleVariant = item.StyleVariant,
    };

    private static FloorPlanTableGeometryDto ToTableDto(Table t) => new()
    {
        Id = t.Id,
        TableNumber = t.TableNumber,
        MaxGuests = t.MaxGuests,
        IsActive = t.IsActive,
        IsOutdoor = t.IsOutdoor,
        Notes = t.Notes,
        PositionX = t.PositionX,
        PositionY = t.PositionY,
        Width = t.Width,
        Height = t.Height,
        Shape = t.Shape,
        Rotation = t.Rotation,
    };

    /// <summary>Builds a persisted wall (+ openings) from the DTO, clamping vertices into bounds.</summary>
    public static FloorPlanWall BuildWall(FloorPlanWallDto dto, decimal width, decimal height, int zIndex, string createdBy)
    {
        var points = dto.Points
            .Select(p => new { x = Clamp(p.X, 0m, width), y = Clamp(p.Y, 0m, height) })
            .ToList();

        return new FloorPlanWall
        {
            PointsJson = JsonSerializer.Serialize(points, PointsJsonOptions),
            ThicknessMeters = Clamp(dto.ThicknessMeters, 0.02m, 1.0m),
            IsClosed = dto.IsClosed,
            RoomName = dto.RoomName,
            FloorStyle = dto.FloorStyle,
            ZIndex = zIndex,
            CreatedBy = createdBy,
            Openings = dto.Openings.Select(o => new FloorPlanOpening
            {
                SegmentIndex = Math.Max(0, o.SegmentIndex),
                OffsetMeters = Math.Max(0m, o.OffsetMeters),
                WidthMeters = Math.Max(0.05m, o.WidthMeters),
                Kind = o.Kind,
                SwingDirection = string.IsNullOrWhiteSpace(o.SwingDirection) ? "none" : o.SwingDirection,
                CreatedBy = createdBy,
            }).ToList(),
        };
    }

    /// <summary>Builds a persisted item from the DTO, clamping its centre into bounds.</summary>
    public static FloorPlanItem BuildItem(FloorPlanItemDto dto, decimal width, decimal height, int zIndex, string createdBy) => new()
    {
        Kind = dto.Kind,
        X = Clamp(dto.X, 0m, width),
        Y = Clamp(dto.Y, 0m, height),
        WidthMeters = Clamp(dto.WidthMeters, 0.1m, width),
        HeightMeters = Clamp(dto.HeightMeters, 0.1m, height),
        RotationDegrees = NormalizeAngle(dto.RotationDegrees),
        ZIndex = zIndex,
        Label = dto.Label,
        StyleVariant = dto.StyleVariant,
        CreatedBy = createdBy,
    };

    /// <summary>Applies geometry from the DTO onto a stored table, clamped into bounds.</summary>
    public static void ApplyTableGeometry(Table table, FloorPlanTableGeometryDto dto, decimal width, decimal height)
    {
        table.PositionX = Clamp(dto.PositionX, 0m, width);
        table.PositionY = Clamp(dto.PositionY, 0m, height);
        table.Width = Clamp(dto.Width, 0.1m, width);
        table.Height = Clamp(dto.Height, 0.1m, height);
        table.Shape = dto.Shape;
        table.Rotation = ((dto.Rotation % 360) + 360) % 360;
    }

    private static List<FloorPlanPointDto> DeserializePoints(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<FloorPlanPointDto>();
        }

        return JsonSerializer.Deserialize<List<FloorPlanPointDto>>(json, PointsJsonOptions)
               ?? new List<FloorPlanPointDto>();
    }

    private static decimal Clamp(decimal value, decimal min, decimal max) =>
        value < min ? min : value > max ? max : value;

    private static decimal NormalizeAngle(decimal degrees)
    {
        var mod = degrees % 360m;
        return mod < 0 ? mod + 360m : mod;
    }
}
