namespace RestaurantSystem.Api.Features.Orders.Dtos;

public enum StaffRoundOperationLookupStatus
{
    Unknown = 0,
    Committed = 1
}

public sealed record StaffRoundOperationLookupDto
{
    public Guid OperationId { get; init; }
    public StaffRoundOperationLookupStatus Status { get; init; }
    public OrderDto? Order { get; init; }
}
