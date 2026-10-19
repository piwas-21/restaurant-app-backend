namespace RestaurantSystem.Api.Features.User.Dtos;

public sealed record CustomerAccountDeletionDto(int SchemaVersion, string State, DateTime? ScheduledAtUtc);
