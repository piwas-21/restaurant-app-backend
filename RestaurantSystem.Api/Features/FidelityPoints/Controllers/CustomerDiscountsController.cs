using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.FidelityPoints.Dtos;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.FidelityPoints.Controllers;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "Admin,Server")]
public class CustomerDiscountsController : ControllerBase
{
    private readonly ICustomerDiscountService _discountService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CustomerDiscountsController> _logger;

    public CustomerDiscountsController(
        ICustomerDiscountService discountService,
        ApplicationDbContext context,
        ILogger<CustomerDiscountsController> logger)
    {
        _discountService = discountService;
        _context = context;
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
        List<CustomerDiscountRule> discounts;

        if (userId.HasValue)
        {
            discounts = activeOnly
                ? await _discountService.GetActiveDiscountsForUserAsync(userId.Value, cancellationToken)
                : await _discountService.GetAllDiscountsForUserAsync(userId.Value, cancellationToken);
        }
        else
        {
            // Get all discounts from all users
            var query = _context.CustomerDiscountRules.AsNoTracking();

            if (activeOnly)
            {
                var now = DateTime.UtcNow;
                query = query.Where(d => d.IsActive
                    && (d.ValidFrom == null || d.ValidFrom <= now)
                    && (d.ValidUntil == null || d.ValidUntil >= now)
                    && (d.MaxUsageCount == null || d.UsageCount < d.MaxUsageCount));
            }

            discounts = await query
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        // Get user info for all discounts
        var userIds = discounts.Select(d => d.UserId).Distinct().ToList();
        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName })
            .ToListAsync(cancellationToken);

        var userDict = users.ToDictionary(u => u.Id, u => (u.Email, u.FirstName, u.LastName));

        var dtos = discounts.Select(d =>
        {
            var (email, firstName, lastName) = userDict.TryGetValue(d.UserId, out var user)
                ? user
                : ("Unknown", "Unknown", "");

            return new CustomerDiscountRuleDto
            {
                Id = d.Id,
                UserId = d.UserId,
                UserEmail = email,
                UserName = $"{firstName} {lastName}".Trim(),
                Name = d.Name,
                DiscountType = d.DiscountType.ToString(),
                DiscountValue = d.DiscountValue,
                MinOrderAmount = d.MinOrderAmount,
                MaxOrderAmount = d.MaxOrderAmount,
                MaxUsageCount = d.MaxUsageCount,
                UsageCount = d.UsageCount,
                IsActive = d.IsActive,
                ValidFrom = d.ValidFrom,
                ValidUntil = d.ValidUntil,
                CreatedAt = d.CreatedAt
            };
        }).ToList();

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

        // Get user info
        var user = await _context.Users
            .Where(u => u.Id == discount.UserId)
            .Select(u => new { u.Email, u.FirstName, u.LastName })
            .FirstOrDefaultAsync(cancellationToken);

        var dto = new CustomerDiscountRuleDto
        {
            Id = discount.Id,
            UserId = discount.UserId,
            UserEmail = user?.Email ?? "Unknown",
            UserName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "Unknown",
            Name = discount.Name,
            DiscountType = discount.DiscountType.ToString(),
            DiscountValue = discount.DiscountValue,
            MinOrderAmount = discount.MinOrderAmount,
            MaxOrderAmount = discount.MaxOrderAmount,
            MaxUsageCount = discount.MaxUsageCount,
            UsageCount = discount.UsageCount,
            IsActive = discount.IsActive,
            ValidFrom = discount.ValidFrom,
            ValidUntil = discount.ValidUntil,
            CreatedAt = discount.CreatedAt
        };

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
        // Validate user exists
        var userExists = await _context.Users.AnyAsync(u => u.Id == dto.UserId, cancellationToken);
        if (!userExists)
            return BadRequest(ApiResponse<object>.Failure("User not found"));

        // Parse discount type
        if (!Enum.TryParse<DiscountType>(dto.DiscountType, out var discountType))
            return BadRequest(ApiResponse<object>.Failure("Invalid discount type. Use 'Percentage' or 'FixedAmount'"));

        var discount = new CustomerDiscountRule
        {
            UserId = dto.UserId,
            Name = dto.Name,
            DiscountType = discountType,
            DiscountValue = dto.DiscountValue,
            MinOrderAmount = dto.MinOrderAmount,
            MaxOrderAmount = dto.MaxOrderAmount,
            MaxUsageCount = dto.MaxUsageCount,
            IsActive = dto.IsActive,
            ValidFrom = dto.ValidFrom,
            ValidUntil = dto.ValidUntil,
            CreatedBy = "System"
        };

        try
        {
            var createdDiscount = await _discountService.CreateDiscountAsync(discount, cancellationToken);

            // Get user info
            var user = await _context.Users
                .Where(u => u.Id == createdDiscount.UserId)
                .Select(u => new { u.Email, u.FirstName, u.LastName })
                .FirstOrDefaultAsync(cancellationToken);

            var responseDto = new CustomerDiscountRuleDto
            {
                Id = createdDiscount.Id,
                UserId = createdDiscount.UserId,
                UserEmail = user?.Email ?? "Unknown",
                UserName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "Unknown",
                Name = createdDiscount.Name,
                DiscountType = createdDiscount.DiscountType.ToString(),
                DiscountValue = createdDiscount.DiscountValue,
                MinOrderAmount = createdDiscount.MinOrderAmount,
                MaxOrderAmount = createdDiscount.MaxOrderAmount,
                MaxUsageCount = createdDiscount.MaxUsageCount,
                UsageCount = createdDiscount.UsageCount,
                IsActive = createdDiscount.IsActive,
                ValidFrom = createdDiscount.ValidFrom,
                ValidUntil = createdDiscount.ValidUntil,
                CreatedAt = createdDiscount.CreatedAt
            };

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
        // Parse discount type
        if (!Enum.TryParse<DiscountType>(dto.DiscountType, out var discountType))
            return BadRequest(ApiResponse<object>.Failure("Invalid discount type. Use 'Percentage' or 'FixedAmount'"));

        var discount = new CustomerDiscountRule
        {
            Id = id,
            Name = dto.Name,
            DiscountType = discountType,
            DiscountValue = dto.DiscountValue,
            MinOrderAmount = dto.MinOrderAmount,
            MaxOrderAmount = dto.MaxOrderAmount,
            MaxUsageCount = dto.MaxUsageCount,
            IsActive = dto.IsActive,
            ValidFrom = dto.ValidFrom,
            ValidUntil = dto.ValidUntil,
            CreatedBy = "System"
        };

        try
        {
            var updatedDiscount = await _discountService.UpdateDiscountAsync(discount, cancellationToken);

            // Get user info
            var user = await _context.Users
                .Where(u => u.Id == updatedDiscount.UserId)
                .Select(u => new { u.Email, u.FirstName, u.LastName })
                .FirstOrDefaultAsync(cancellationToken);

            var responseDto = new CustomerDiscountRuleDto
            {
                Id = updatedDiscount.Id,
                UserId = updatedDiscount.UserId,
                UserEmail = user?.Email ?? "Unknown",
                UserName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "Unknown",
                Name = updatedDiscount.Name,
                DiscountType = updatedDiscount.DiscountType.ToString(),
                DiscountValue = updatedDiscount.DiscountValue,
                MinOrderAmount = updatedDiscount.MinOrderAmount,
                MaxOrderAmount = updatedDiscount.MaxOrderAmount,
                MaxUsageCount = updatedDiscount.MaxUsageCount,
                UsageCount = updatedDiscount.UsageCount,
                IsActive = updatedDiscount.IsActive,
                ValidFrom = updatedDiscount.ValidFrom,
                ValidUntil = updatedDiscount.ValidUntil,
                CreatedAt = updatedDiscount.CreatedAt
            };

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
