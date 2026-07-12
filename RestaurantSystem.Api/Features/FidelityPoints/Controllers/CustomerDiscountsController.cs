using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.FidelityPoints.Dtos;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Mapping;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FidelityPoints.Controllers;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "Admin,Server")]
public class CustomerDiscountsController : ControllerBase
{
    private readonly ICustomerDiscountService _discountService;
    private readonly ILogger<CustomerDiscountsController> _logger;

    public CustomerDiscountsController(
        ICustomerDiscountService discountService,
        ILogger<CustomerDiscountsController> logger)
    {
        _discountService = discountService;
        _logger = logger;
    }

    /// <summary>
    /// Get all customer discount rules
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<CustomerDiscountRuleDto>>), 200)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? userId = null,
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var dtos = await _discountService.GetDiscountRuleDtosAsync(userId, activeOnly, cancellationToken);
        return Ok(ApiResponse<List<CustomerDiscountRuleDto>>.SuccessWithData(dtos));
    }

    /// <summary>
    /// Get a specific customer discount rule by ID
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<CustomerDiscountRuleDto>), 200)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var discount = await _discountService.GetDiscountByIdAsync(id, cancellationToken);

        if (discount == null)
            return NotFound(ApiResponse<object>.Failure("Customer discount not found"));

        var dto = await _discountService.ToDtoAsync(discount, cancellationToken);
        return Ok(ApiResponse<CustomerDiscountRuleDto>.SuccessWithData(dto));
    }

    /// <summary>
    /// Create a new customer discount rule (Admin only)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<CustomerDiscountRuleDto>), 201)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCustomerDiscountRuleDto dto,
        CancellationToken cancellationToken)
    {
        if (!await _discountService.UserExistsAsync(dto.UserId, cancellationToken))
            return BadRequest(ApiResponse<object>.Failure("User not found"));

        if (!Enum.TryParse<DiscountType>(dto.DiscountType, out var discountType))
            return BadRequest(ApiResponse<object>.Failure("Invalid discount type. Use 'Percentage' or 'FixedAmount'"));

        var discount = CustomerDiscountRuleMapper.ToEntity(dto, discountType);

        try
        {
            var createdDiscount = await _discountService.CreateDiscountAsync(discount, cancellationToken);
            var responseDto = await _discountService.ToDtoAsync(createdDiscount, cancellationToken);

            _logger.LogInformation("Created customer discount: {DiscountName} for user {UserId}",
                createdDiscount.Name, createdDiscount.UserId);

            return CreatedAtAction(
                nameof(GetById),
                new { id = createdDiscount.Id },
                ApiResponse<CustomerDiscountRuleDto>.SuccessWithData(responseDto, "Customer discount created successfully"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Failure(ex.Message));
        }
    }

    /// <summary>
    /// Update an existing customer discount rule (Admin only)
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<CustomerDiscountRuleDto>), 200)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateCustomerDiscountRuleDto dto,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<DiscountType>(dto.DiscountType, out var discountType))
            return BadRequest(ApiResponse<object>.Failure("Invalid discount type. Use 'Percentage' or 'FixedAmount'"));

        var discount = CustomerDiscountRuleMapper.ToEntity(id, dto, discountType);

        try
        {
            var updatedDiscount = await _discountService.UpdateDiscountAsync(discount, cancellationToken);
            var responseDto = await _discountService.ToDtoAsync(updatedDiscount, cancellationToken);

            _logger.LogInformation("Updated customer discount: {DiscountName}", updatedDiscount.Name);

            return Ok(ApiResponse<CustomerDiscountRuleDto>.SuccessWithData(responseDto, "Customer discount updated successfully"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Failure(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<object>.Failure(ex.Message));
        }
    }

    /// <summary>
    /// Delete (deactivate) a customer discount rule (Admin only)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _discountService.DeleteDiscountAsync(id, cancellationToken);
            _logger.LogInformation("Deleted customer discount: {DiscountId}", id);
            return Ok(ApiResponse<object>.SuccessWithoutData("Customer discount deleted successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<object>.Failure(ex.Message));
        }
    }
}
