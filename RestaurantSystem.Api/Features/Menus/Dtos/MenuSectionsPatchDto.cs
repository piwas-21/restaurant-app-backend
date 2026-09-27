using RestaurantSystem.Api.Features.Products.Dtos;

namespace RestaurantSystem.Api.Features.Menus.Dtos;

public sealed record MenuSectionsPatchDto
{
    /// <summary>Omitted means leave the collection unchanged; an empty list removes every section.</summary>
    public List<MenuSectionDto>? Sections { get; init; }
}

public sealed record MenuSectionsPatchResultDto
{
    public int AuthoringVersion { get; init; }
    public List<MenuSectionDto> Sections { get; init; } = [];
}
