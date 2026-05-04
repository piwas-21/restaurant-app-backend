using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Groups.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Groups;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class GroupDiscountController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GroupDiscountController(ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<GroupDiscountDto>>> CreateDiscount([FromBody] CreateGroupDiscountDto dto, [FromQuery] Guid groupId)
    {
        var group = await _context.UserGroups.FindAsync(groupId);
        if (group == null)
        {
            return NotFound(ApiResponse<GroupDiscountDto>.Failure("Group not found"));
        }

        var discount = new GroupDiscount
        {
            GroupId = groupId,
            Name = dto.Name,
            Type = dto.Type,
            Value = dto.Value,
            MinimumOrderAmount = dto.MinimumOrderAmount,
            MaximumDiscountAmount = dto.MaximumDiscountAmount,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };

        _context.GroupDiscounts.Add(discount);
        await _context.SaveChangesAsync();

        var result = new GroupDiscountDto
        {
            Id = discount.Id,
            GroupId = discount.GroupId,
            Name = discount.Name,
            Type = discount.Type,
            Value = discount.Value,
            MinimumOrderAmount = discount.MinimumOrderAmount,
            MaximumDiscountAmount = discount.MaximumDiscountAmount,
            IsActive = discount.IsActive
        };

        return Ok(ApiResponse<GroupDiscountDto>.SuccessWithData(result, "Discount created successfully"));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<GroupDiscountDto>>> UpdateDiscount(Guid id, [FromBody] UpdateGroupDiscountDto dto)
    {
        if (id != dto.Id)
        {
            return BadRequest(ApiResponse<GroupDiscountDto>.Failure("ID mismatch"));
        }

        var discount = await _context.GroupDiscounts.FindAsync(id);
        if (discount == null)
        {
            return NotFound(ApiResponse<GroupDiscountDto>.Failure("Discount not found"));
        }

        discount.Name = dto.Name;
        discount.Type = dto.Type;
        discount.Value = dto.Value;
        discount.MinimumOrderAmount = dto.MinimumOrderAmount;
        discount.MaximumDiscountAmount = dto.MaximumDiscountAmount;
        discount.IsActive = dto.IsActive;
        discount.UpdatedAt = DateTime.UtcNow;
        discount.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync();

        var result = new GroupDiscountDto
        {
            Id = discount.Id,
            GroupId = discount.GroupId,
            Name = discount.Name,
            Type = discount.Type,
            Value = discount.Value,
            MinimumOrderAmount = discount.MinimumOrderAmount,
            MaximumDiscountAmount = discount.MaximumDiscountAmount,
            IsActive = discount.IsActive
        };

        return Ok(ApiResponse<GroupDiscountDto>.SuccessWithData(result, "Discount updated successfully"));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteDiscount(Guid id)
    {
        var discount = await _context.GroupDiscounts.FindAsync(id);
        if (discount == null)
        {
            return NotFound(ApiResponse<bool>.Failure("Discount not found"));
        }

        _context.GroupDiscounts.Remove(discount);
        await _context.SaveChangesAsync();

        return Ok(ApiResponse<bool>.SuccessWithData(true, "Discount deleted successfully"));
    }

    [HttpGet("group/{groupId}")]
    public async Task<ActionResult<ApiResponse<List<GroupDiscountDto>>>> GetGroupDiscounts(Guid groupId)
    {
        var discounts = await _context.GroupDiscounts
            .Where(d => d.GroupId == groupId)
            .Select(d => new GroupDiscountDto
            {
                Id = d.Id,
                GroupId = d.GroupId,
                Name = d.Name,
                Type = d.Type,
                Value = d.Value,
                MinimumOrderAmount = d.MinimumOrderAmount,
                MaximumDiscountAmount = d.MaximumDiscountAmount,
                IsActive = d.IsActive
            })
            .ToListAsync();

        return Ok(ApiResponse<List<GroupDiscountDto>>.SuccessWithData(discounts));
    }
}
