using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.User.Commands.RegisterCustomerCommand;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.User;

/// <summary>
/// Pins the registration endpoint's machine-readable failure contract for
/// the duplicate-email path. Frontend (PR #70) relies on a stable
/// camelCase <c>errorCode == "EmailAlreadyExists"</c> on the response
/// envelope to surface an inline hint without substring-matching the
/// English error string. Closes #63.
/// </summary>
public class RegisterCustomerErrorCodeTests : IntegrationTestBase
{
    public RegisterCustomerErrorCodeTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task RegisterCustomer_DuplicateEmail_ReturnsEmailAlreadyExistsErrorCode()
    {
        const string existingEmail = "duplicate.email.test@example.com";

        // Seed an existing customer with the email we'll then collide with.
        // Going through UserManager mirrors the real registration path
        // (Identity stamps NormalizedEmail/PasswordHash/SecurityStamp).
        using (var scope = Factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();

            var existing = new ApplicationUser
            {
                Email = existingEmail,
                UserName = existingEmail,
                FirstName = "Existing",
                LastName = "User",
                Role = UserRole.Customer,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "RegisterCustomerErrorCodeTests",
                RefreshToken = string.Empty,
            };
            var createResult = await userManager.CreateAsync(existing, "ValidPass123!");
            createResult.Succeeded.Should().BeTrue(
                "test setup requires the colliding user to be seeded first");
        }

        var command = new RegisterCustomerCommand(
            FirstName: "New",
            LastName: "Customer",
            Email: existingEmail,
            Password: "ValidPass123!",
            ConfirmPassword: "ValidPass123!");

        var response = await PostAsJsonAsync("/api/User/register/customer", command);

        // The endpoint returns HTTP 200 with the failure envelope today
        // (see UserController.RegisterCustomer). This test pins the
        // *envelope* contract, not the status code.
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rawJson = await response.Content.ReadAsStringAsync();

        // Wire-shape assertion: the property MUST be camelCase "errorCode"
        // with the exact string the frontend pattern-matches against.
        // (The Program.cs AddJsonOptions pipeline does NOT set a global
        // DefaultIgnoreCondition — the property's [JsonIgnore(...WhenWritingNull)]
        // attribute is the load-bearing piece keeping nulls off the wire.)
        rawJson.Should().Contain("\"errorCode\":\"EmailAlreadyExists\"",
            "frontend duplicateEmailDetection matches this exact camelCase code");

        var envelope = JsonSerializer.Deserialize<ApiResponse<AuthResponse>>(rawJson, JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeFalse();
        envelope.ErrorCode.Should().Be(ErrorCodes.EmailAlreadyExists);
        // Human-readable message stays populated for older clients / logs.
        envelope.Message.Should().NotBeNullOrWhiteSpace();
        envelope.Errors.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RegisterCustomer_NewEmail_DoesNotEmitErrorCodeOnTheWire()
    {
        var command = new RegisterCustomerCommand(
            FirstName: "Fresh",
            LastName: "Customer",
            Email: $"fresh.{Guid.NewGuid():N}@example.com",
            Password: "ValidPass123!",
            ConfirmPassword: "ValidPass123!");

        var response = await PostAsJsonAsync("/api/User/register/customer", command);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rawJson = await response.Content.ReadAsStringAsync();

        // The success path leaves ErrorCode null. The JsonIgnore-when-null
        // attribute on ApiResponse.ErrorCode must keep the field off the
        // wire — otherwise an opportunistic frontend null-check would
        // surface phantom errors. Pin both the absence of the key and a
        // sanity check that the envelope still parses as a success.
        rawJson.Should().NotContain("errorCode",
            "ErrorCode is JsonIgnore-when-null; a null value must not appear on success responses");

        var envelope = JsonSerializer.Deserialize<ApiResponse<AuthResponse>>(rawJson, JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.ErrorCode.Should().BeNull();
    }
}
