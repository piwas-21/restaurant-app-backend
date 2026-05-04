using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Commands.ChangePasswordCommand;
using RestaurantSystem.Api.Features.Auth.Commands.ForgotPasswordCommand;
using RestaurantSystem.Api.Features.Auth.Commands.LoginCommand;
using RestaurantSystem.Api.Features.Auth.Commands.RefreshTokenCommand;
using RestaurantSystem.Api.Features.Auth.Commands.ResetPasswordCommand;
using RestaurantSystem.Api.Features.Auth.Commands.SendEmailVerificationCommand;
using RestaurantSystem.Api.Features.Auth.Commands.VerifyEmailCommand;
using RestaurantSystem.Api.Features.Auth.Dtos;

namespace RestaurantSystem.Api.Features.Auth;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public AuthController(CustomMediator mediator)
    {
        _mediator = mediator;
    }


    /// <summary>
    /// User login
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Google login
    /// </summary>
    [HttpPost("google-login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> GoogleLogin([FromBody] GoogleLoginCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Apple login
    /// </summary>
    [HttpPost("apple-login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> AppleLogin([FromBody] AppleLoginCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Refresh access token
    /// </summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> RefreshToken([FromBody] RefreshTokenCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Request password reset
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("forgot-password")]
    public async Task<ActionResult<ApiResponse<string>>> ForgotPassword([FromBody] ForgotPasswordCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Reset password with token
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("forgot-password")]
    public async Task<ActionResult<ApiResponse<string>>> ResetPassword([FromBody] ResetPasswordCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Change password for authenticated user
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<string>>> ChangePassword([FromBody] ChangePasswordCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Send email verification
    /// </summary>
    [HttpPost("send-email-verification")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<string>>> SendEmailVerification([FromBody] SendEmailVerificationCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Verify email address
    /// </summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<string>>> VerifyEmail([FromBody] VerifyEmailCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Test authentication (requires valid JWT token)
    /// </summary>
    [HttpGet("test-auth")]
    [Authorize]
    public ActionResult<ApiResponse<string>> TestAuth()
    {
        return Ok(ApiResponse<string>.SuccessWithData("You are authenticated!"));
    }

    /// <summary>
    /// Admin-only endpoint for testing authorization
    /// </summary>
    [HttpGet("admin-only")]
    [RequireAdmin]
    public ActionResult<ApiResponse<string>> AdminOnly()
    {
        return Ok(ApiResponse<string>.SuccessWithData("You are an admin!"));
    }
}
