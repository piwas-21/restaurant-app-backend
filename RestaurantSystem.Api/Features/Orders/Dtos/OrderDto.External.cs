using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public partial record OrderDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExternalOrderDto? ExternalOrder { get; set; }
}
