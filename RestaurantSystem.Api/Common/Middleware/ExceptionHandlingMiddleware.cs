using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using System.Net;
using System.Text.Json;

namespace RestaurantSystem.Api.Common.Middleware;


public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "An unhandled exception occurred");

        HttpStatusCode statusCode;
        string message;
        // Set only by exceptions that carry a stable discriminator; null leaves ErrorCode off the
        // wire entirely (JsonIgnore-when-null on ApiResponse.ErrorCode).
        string? errorCode = null;
        // Per-rule reasons, when the exception carries them. Null keeps the single-detail shape
        // every other exception has always produced.
        List<string>? reasons = null;

        // Determine status code and message based on exception type
        switch (exception)
        {
            case ForbiddenException:
                statusCode = HttpStatusCode.Forbidden;
                message = exception.Message;
                break;

            case UnauthorizedAccessException:
                statusCode = HttpStatusCode.Unauthorized;
                message = exception.Message;
                break;

            case BadRequestException badRequestEx:
                statusCode = HttpStatusCode.BadRequest;
                message = exception.Message;
                errorCode = badRequestEx.ErrorCode;
                reasons = badRequestEx.Errors?.ToList();
                break;

            case NotFoundException notFoundEx:
                statusCode = HttpStatusCode.NotFound;
                message = exception.Message;
                errorCode = notFoundEx.ErrorCode;
                break;

            case ArgumentException:
                statusCode = HttpStatusCode.BadRequest;
                message = exception.Message;
                break;

            case EmailDeliveryException:
                // Upstream email provider failure, not an internal bug.
                statusCode = HttpStatusCode.BadGateway;
                message = "The email could not be delivered. Please try again later.";
                break;

            default:
                statusCode = HttpStatusCode.InternalServerError;
                message = _environment.IsDevelopment()
                    ? exception.Message
                    : "An error occurred while processing your request";
                break;
        }

        var detail = _environment.IsDevelopment() ? exception.ToString() : message;
        // A refusal that named its individual reasons keeps them, one entry per broken rule; every
        // other exception keeps the single-detail shape.
        //
        // Scoped claim, because the obvious stronger one is false: this makes only REASON-CARRYING
        // refusals environment-stable, since they bypass the detail above. Everything else — every
        // 500, and every handler-thrown BadRequest, NotFound, Forbidden or Unauthorized — still
        // serves the fully-stringified exception as its single error under Development, and the
        // frontend prefers the errors array over the message when displaying. That is pre-existing
        // and deliberately not widened here; both deployed environments pin Production.
        var response = (errorCode, reasons) switch
        {
            (null, null) => ApiResponse<object>.Failure(detail, message),
            (null, not null) => ApiResponse<object>.Failure(reasons, message),
            (not null, null) => ApiResponse<object>.FailureWithCode(detail, errorCode, message),
            (not null, not null) => ApiResponse<object>.FailureWithCode(reasons, errorCode, message),
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }
}

// Extension method for using the middleware
public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
