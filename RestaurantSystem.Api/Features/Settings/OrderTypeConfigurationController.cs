using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Settings.Dtos;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Settings;

[ApiController]
[Route("api/[controller]")]
public class OrderTypeConfigurationController : ControllerBase
{
    private readonly IOrderTypeConfigurationService _service;

    public OrderTypeConfigurationController(IOrderTypeConfigurationService service)
    {
        _service = service;
    }

    /// <summary>
    /// Get all order type configurations (admin only)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ApiResponse<List<OrderTypeConfigurationDto>>> GetAll(CancellationToken cancellationToken)
    {
        var configurations = await _service.GetAllAsync(cancellationToken);
        return ApiResponse<List<OrderTypeConfigurationDto>>.SuccessWithData(configurations);
    }

    /// <summary>
    /// Get enabled order types (public endpoint for customers)
    /// </summary>
    [HttpGet("enabled")]
    [AllowAnonymous]
    public async Task<ApiResponse<List<OrderType>>> GetEnabled(CancellationToken cancellationToken)
    {
        var enabledTypes = await _service.GetEnabledOrderTypesAsync(cancellationToken);
        return ApiResponse<List<OrderType>>.SuccessWithData(enabledTypes);
    }

    /// <summary>
    /// Update order type configuration (admin only)
    /// </summary>
    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<ApiResponse<OrderTypeConfigurationDto>> Update(
        [FromBody] UpdateOrderTypeConfigurationDto dto,
        CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateAsync(dto.OrderType, dto.IsEnabled, cancellationToken);
        return ApiResponse<OrderTypeConfigurationDto>.SuccessWithData(
            updated,
            "Order type configuration updated successfully"
        );
    }
}
