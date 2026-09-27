using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal sealed record MaterializedOptionSetRow(string RowType, Guid Id, object Entity, bool OwnsRow);
