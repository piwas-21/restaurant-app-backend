using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.FidelityPoints.Dtos;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FidelityPoints.Controllers;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "Admin")]
public class PointRulesController : ControllerBase
{
    private readonly IPointEarningRuleService _ruleService;
    private readonly ILogger<PointRulesController> _logger;

    public PointRulesController(
        IPointEarningRuleService ruleService,
        ILogger<PointRulesController> logger)
    {
        _ruleService = ruleService;
        _logger = logger;
    }

    /// <summary>
    /// Get all point earning rules
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<PointEarningRuleDto>>), 200)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var rules = activeOnly
            ? await _ruleService.GetActiveRulesAsync(cancellationToken)
            : await _ruleService.GetAllRulesAsync(cancellationToken);

        var dtos = rules.Select(r => new PointEarningRuleDto
        {
            Id = r.Id,
            Name = r.Name,
            MinOrderAmount = r.MinOrderAmount,
            MaxOrderAmount = r.MaxOrderAmount,
            PointsAwarded = r.PointsAwarded,
            IsActive = r.IsActive,
            Priority = r.Priority,
            CreatedAt = r.CreatedAt
        }).ToList();

        return Ok(ApiResponse<List<PointEarningRuleDto>>.SuccessWithData(dtos));
    }

    /// <summary>
    /// Get a specific point earning rule by ID
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<PointEarningRuleDto>), 200)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var rule = await _ruleService.GetRuleByIdAsync(id, cancellationToken);

        if (rule == null)
            return NotFound(ApiResponse<object>.Failure("Point earning rule not found"));

        var dto = new PointEarningRuleDto
        {
            Id = rule.Id,
            Name = rule.Name,
            MinOrderAmount = rule.MinOrderAmount,
            MaxOrderAmount = rule.MaxOrderAmount,
            PointsAwarded = rule.PointsAwarded,
            IsActive = rule.IsActive,
            Priority = rule.Priority,
            CreatedAt = rule.CreatedAt
        };

        return Ok(ApiResponse<PointEarningRuleDto>.SuccessWithData(dto));
    }

    /// <summary>
    /// Create a new point earning rule
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<PointEarningRuleDto>), 201)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePointEarningRuleDto dto,
        CancellationToken cancellationToken)
    {
        var rule = new PointEarningRule
        {
            Name = dto.Name,
            MinOrderAmount = dto.MinOrderAmount,
            MaxOrderAmount = dto.MaxOrderAmount,
            PointsAwarded = dto.PointsAwarded,
            IsActive = dto.IsActive,
            Priority = dto.Priority,
            CreatedBy = "System"
        };

        try
        {
            var createdRule = await _ruleService.CreateRuleAsync(rule, cancellationToken);

            var responseDto = new PointEarningRuleDto
            {
                Id = createdRule.Id,
                Name = createdRule.Name,
                MinOrderAmount = createdRule.MinOrderAmount,
                MaxOrderAmount = createdRule.MaxOrderAmount,
                PointsAwarded = createdRule.PointsAwarded,
                IsActive = createdRule.IsActive,
                Priority = createdRule.Priority,
                CreatedAt = createdRule.CreatedAt
            };

            _logger.LogInformation("Created point earning rule: {RuleName}", createdRule.Name);

            return CreatedAtAction(
                nameof(GetById),
                new { id = createdRule.Id },
                ApiResponse<PointEarningRuleDto>.SuccessWithData(responseDto, "Point earning rule created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Failure(ex.Message));
        }
    }

    /// <summary>
    /// Update an existing point earning rule
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<PointEarningRuleDto>), 200)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdatePointEarningRuleDto dto,
        CancellationToken cancellationToken)
    {
        var rule = new PointEarningRule
        {
            Id = id,
            Name = dto.Name,
            MinOrderAmount = dto.MinOrderAmount,
            MaxOrderAmount = dto.MaxOrderAmount,
            PointsAwarded = dto.PointsAwarded,
            IsActive = dto.IsActive,
            Priority = dto.Priority,
            CreatedBy = "System"
        };

        try
        {
            var updatedRule = await _ruleService.UpdateRuleAsync(rule, cancellationToken);

            var responseDto = new PointEarningRuleDto
            {
                Id = updatedRule.Id,
                Name = updatedRule.Name,
                MinOrderAmount = updatedRule.MinOrderAmount,
                MaxOrderAmount = updatedRule.MaxOrderAmount,
                PointsAwarded = updatedRule.PointsAwarded,
                IsActive = updatedRule.IsActive,
                Priority = updatedRule.Priority,
                CreatedAt = updatedRule.CreatedAt
            };

            _logger.LogInformation("Updated point earning rule: {RuleName}", updatedRule.Name);

            return Ok(ApiResponse<PointEarningRuleDto>.SuccessWithData(responseDto, "Point earning rule updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Failure(ex.Message));
        }
    }

    /// <summary>
    /// Delete a point earning rule
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _ruleService.DeleteRuleAsync(id, cancellationToken);
            _logger.LogInformation("Deleted point earning rule: {RuleId}", id);
            return Ok(ApiResponse<object>.SuccessWithoutData("Point earning rule deleted successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<object>.Failure(ex.Message));
        }
    }

    /// <summary>
    /// Validate if a rule would overlap with existing rules
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(ApiResponse<bool>), 200)]
    public async Task<IActionResult> Validate(
        [FromBody] CreatePointEarningRuleDto dto,
        CancellationToken cancellationToken)
    {
        var rule = new PointEarningRule
        {
            MinOrderAmount = dto.MinOrderAmount,
            MaxOrderAmount = dto.MaxOrderAmount,
            IsActive = dto.IsActive,
            CreatedBy = "System"
        };

        var isValid = await _ruleService.ValidateNoOverlapAsync(rule, cancellationToken);

        return Ok(ApiResponse<bool>.SuccessWithData(
            isValid,
            isValid ? "Rule is valid" : "Rule overlaps with existing rules"));
    }
}
