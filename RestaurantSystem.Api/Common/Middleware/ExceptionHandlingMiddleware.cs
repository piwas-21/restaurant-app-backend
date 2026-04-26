using FluentValidation;
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

        // Determine status code and message based on exception type
        switch (exception)
        {
            case ValidationException validationEx:
                statusCode = HttpStatusCode.BadRequest;
                message = "Validation failed";
                var errors = validationEx.Errors.Select(e => e.ErrorMessage).ToList();
                var validationResponse = ApiResponse<object>.Failure(errors, message);
                context.Response.ContentType = "application/json";
                context.Response.StatusCode = (int)statusCode;
                await JsonSerializer.SerializeAsync(context.Response.Body, validationResponse, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                return;

            case ForbiddenException:
                statusCode = HttpStatusCode.Forbidden;
                message = exception.Message;
                break;

            case UnauthorizedAccessException:
                statusCode = HttpStatusCode.Unauthorized;
                message = exception.Message;
                break;

            case ArgumentException:
                statusCode = HttpStatusCode.BadRequest;
                message = exception.Message;
                break;

            default:
                statusCode = HttpStatusCode.InternalServerError;
                message = _environment.IsDevelopment()
                    ? exception.Message
                    : "An error occurred while processing your request";
                break;
        }

        var response = ApiResponse<object>.Failure(
            _environment.IsDevelopment() ? exception.ToString() : message,
            message
        );

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
