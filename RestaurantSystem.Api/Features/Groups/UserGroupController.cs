using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Groups.Dtos;
using RestaurantSystem.Api.Features.Groups.Interfaces;

namespace RestaurantSystem.Api.Features.Groups;

[ApiController]
[Route("api/[controller]")]
public class UserGroupController : ControllerBase
{
    private readonly IUserGroupService _userGroupService;

    public UserGroupController(IUserGroupService userGroupService)
    {
        _userGroupService = userGroupService;
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<UserGroupDto>>> CreateGroup([FromBody] CreateUserGroupDto dto)
    {
        var group = await _userGroupService.CreateGroupAsync(dto);
        return Ok(ApiResponse<UserGroupDto>.SuccessWithData(group, "Group created successfully"));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<UserGroupDto>>> UpdateGroup(Guid id, [FromBody] UpdateUserGroupDto dto)
    {
        if (id != dto.Id)
        {
            return BadRequest(ApiResponse<UserGroupDto>.Failure("ID mismatch"));
        }

        var group = await _userGroupService.UpdateGroupAsync(dto);
        return Ok(ApiResponse<UserGroupDto>.SuccessWithData(group, "Group updated successfully"));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteGroup(Guid id)
    {
        await _userGroupService.DeleteGroupAsync(id);
        return Ok(ApiResponse<bool>.SuccessWithData(true, "Group deleted successfully"));
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<UserGroupDto>>> GetGroup(Guid id)
    {
        var group = await _userGroupService.GetGroupByIdAsync(id);
        if (group == null)
        {
            return NotFound(ApiResponse<UserGroupDto>.Failure("Group not found"));
        }

        return Ok(ApiResponse<UserGroupDto>.SuccessWithData(group));
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<List<UserGroupDto>>>> GetAllGroups()
    {
        var groups = await _userGroupService.GetAllGroupsAsync();
        return Ok(ApiResponse<List<UserGroupDto>>.SuccessWithData(groups));
    }

    [HttpPost("{groupId}/members")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<GroupMembershipDto>>> AddMember(Guid groupId, [FromBody] AddMemberDto dto)
    {
        var membership = await _userGroupService.AddMemberAsync(groupId, dto);
        return Ok(ApiResponse<GroupMembershipDto>.SuccessWithData(membership, "Member added successfully"));
    }

    [HttpDelete("{groupId}/members/{userId}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<bool>>> RemoveMember(Guid groupId, Guid userId)
    {
        await _userGroupService.RemoveMemberAsync(groupId, userId);
        return Ok(ApiResponse<bool>.SuccessWithData(true, "Member removed successfully"));
    }

    [HttpGet("{groupId}/members")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<List<GroupMembershipDto>>>> GetGroupMembers(Guid groupId)
    {
        var members = await _userGroupService.GetGroupMembersAsync(groupId);
        return Ok(ApiResponse<List<GroupMembershipDto>>.SuccessWithData(members));
    }

    [HttpGet("membership/{membershipId}/qrcode")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetMemberQRCode(Guid membershipId)
    {
        var qrCodeImage = await _userGroupService.GetMemberQRCodeImageAsync(membershipId);
        return File(qrCodeImage, "image/png");
    }

    [HttpPost("validate-qr")]
    [AllowAnonymous] // Cashiers and public can validate
    public async Task<ActionResult<ApiResponse<QRCodeValidationResult>>> ValidateQRCode([FromBody] ValidateQRCodeDto dto)
    {
        var result = await _userGroupService.ValidateMembershipByQRCodeAsync(dto.QRCode);
        return Ok(ApiResponse<QRCodeValidationResult>.SuccessWithData(result));
    }

    [HttpGet("membership/{membershipId}/discount")]
    [Authorize(Roles = "Admin,Cashier")]
    public async Task<ActionResult<ApiResponse<decimal>>> CalculateDiscount(Guid membershipId, [FromQuery] decimal orderAmount)
    {
        var discount = await _userGroupService.CalculateDiscountAsync(membershipId, orderAmount);
        return Ok(ApiResponse<decimal>.SuccessWithData(discount));
    }
}
