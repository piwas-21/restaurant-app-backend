using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;

namespace RestaurantSystem.Api.Features.FidelityPoints.Controllers;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "Admin")]
public class FidelityAnalyticsController : ControllerBase
{
    private readonly IFidelityPointsService _fidelityService;
    private readonly IPointEarningRuleService _ruleService;
    private readonly ICustomerDiscountService _discountService;
    private readonly ILogger<FidelityAnalyticsController> _logger;

    public FidelityAnalyticsController(
        IFidelityPointsService fidelityService,
        IPointEarningRuleService ruleService,
        ICustomerDiscountService discountService,
        ILogger<FidelityAnalyticsController> logger)
    {
        _fidelityService = fidelityService;
        _ruleService = ruleService;
        _discountService = discountService;
        _logger = logger;
    }

    /// <summary>
    /// Get comprehensive fidelity system analytics
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<FidelityAnalyticsDto>), 200)]
    public async Task<IActionResult> GetAnalytics()
    {
        try
        {
            var analytics = await _fidelityService.GetSystemAnalyticsAsync();
            var activeRulesCount = await _ruleService.GetActiveRulesCountAsync();
            var activeDiscountsCount = await _discountService.GetActiveDiscountsCountAsync();

            var result = new FidelityAnalyticsDto
            {
                TotalPointsIssued = analytics.TotalPointsIssued,
                TotalPointsRedeemed = analytics.TotalPointsRedeemed,
                TotalActiveUsers = analytics.TotalActiveUsers,
                TotalPointsOutstanding = analytics.TotalPointsOutstanding,
                AveragePointsPerUser = analytics.AveragePointsPerUser,
                TotalDiscountGiven = analytics.TotalDiscountGiven,
                ActivePointRules = activeRulesCount,
                ActiveCustomerDiscounts = activeDiscountsCount,
                RecentTransactionsCount = analytics.RecentTransactionsCount
            };

            return Ok(ApiResponse<FidelityAnalyticsDto>.SuccessWithData(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching fidelity analytics");
            return StatusCode(500, ApiResponse<FidelityAnalyticsDto>.Failure(
                "An error occurred while fetching analytics"));
        }
    }
}

/// <summary>
/// Fidelity Analytics DTO
/// </summary>
public class FidelityAnalyticsDto
{
    public int TotalPointsIssued { get; set; }
    public int TotalPointsRedeemed { get; set; }
    public int TotalActiveUsers { get; set; }
    public int TotalPointsOutstanding { get; set; }
    public decimal AveragePointsPerUser { get; set; }
    public decimal TotalDiscountGiven { get; set; }
    public int ActivePointRules { get; set; }
    public int ActiveCustomerDiscounts { get; set; }
    public int RecentTransactionsCount { get; set; }
}
