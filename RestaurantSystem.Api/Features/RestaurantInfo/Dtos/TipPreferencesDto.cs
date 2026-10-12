namespace RestaurantSystem.Api.Features.RestaurantInfo.Dtos;

/// <summary>Fixed amounts use currency minor units; percentage values are whole percentages.</summary>
public sealed record TipPreferencesDto(string Mode, IReadOnlyList<int> Values, string? Currency);
