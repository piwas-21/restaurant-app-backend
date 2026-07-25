using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.FloorPlan.Dtos;
using RestaurantSystem.Api.Features.FloorPlan.Services;

namespace RestaurantSystem.Api.Features.FloorPlan.Queries.GetFloorPlanQuery;

/// <summary>The default plan the anonymous guest map renders (one payload, §5.2).</summary>
public record GetFloorPlanQuery : IQuery<ApiResponse<FloorPlanDocumentDto>>;

public class GetFloorPlanQueryHandler : IQueryHandler<GetFloorPlanQuery, ApiResponse<FloorPlanDocumentDto>>
{
    private readonly IFloorPlanService _service;
    private readonly ILogger<GetFloorPlanQueryHandler> _logger;

    public GetFloorPlanQueryHandler(IFloorPlanService service, ILogger<GetFloorPlanQueryHandler> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task<ApiResponse<FloorPlanDocumentDto>> Handle(GetFloorPlanQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _service.GetDefaultAsync(cancellationToken);
            return document is null
                ? ApiResponse<FloorPlanDocumentDto>.Failure("No floor plan is configured")
                : ApiResponse<FloorPlanDocumentDto>.SuccessWithData(document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading floor plan");
            return ApiResponse<FloorPlanDocumentDto>.Failure("Failed to load floor plan");
        }
    }
}
