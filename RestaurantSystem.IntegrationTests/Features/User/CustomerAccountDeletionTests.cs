using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Api.Features.User.Commands.RequestAccountDeletionCommand;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.User;

[Collection("Database Lane 4")]
public sealed class CustomerAccountDeletionTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string NativePath = "/api/customer-mobile/account/deletion";
    private readonly Mock<IEmailService> _emails = new();
    private readonly Mock<ILogger<RequestAccountDeletionCommandHandler>> _logs = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<AuthorizationOptions>(options => options.DefaultPolicy =
            new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build());
        services.AddSingleton(_logs.Object);
        services.RemoveAll<IEmailService>();
        services.AddSingleton(_emails.Object);
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        });
    }

    [Fact]
    public async Task Repeated_request_preserves_original_deadline_and_does_not_send_another_email()
    {
        var owner = await CustomerAsync();
        var scheduled = DateTime.UtcNow.AddDays(7);
        await using (var context = DatabaseFixture.CreateContext())
        {
            (await context.Users.SingleAsync(value => value.Id == owner)).DeletionScheduledAt = scheduled;
            await context.SaveChangesAsync();
            scheduled = (await context.Users.AsNoTracking().SingleAsync(value => value.Id == owner)).DeletionScheduledAt!.Value;
        }
        using var response = await Client.PostAsync("/api/user/request-deletion", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadResponseAsync<ApiResponse<string>>(response))!.Success.Should().BeTrue();
        await using var read = DatabaseFixture.CreateContext();
        (await read.Users.SingleAsync(value => value.Id == owner)).DeletionScheduledAt.Should().Be(scheduled);
        _emails.Invocations.Count.Should().Be(0);
    }

    [Fact]
    public async Task Native_request_and_readback_expose_only_the_original_customer_schedule()
    {
        var owner = await CustomerAsync();
        var foreign = await CustomerAsync(select: false);
        using var before = await Client.GetAsync(NativePath);
        before.StatusCode.Should().Be(HttpStatusCode.OK, await before.Content.ReadAsStringAsync());
        var initial = (await before.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
        initial.GetProperty("state").GetString().Should().Be("not-requested");
        var startedAt = DateTime.UtcNow;
        using var requested = await Client.PostAsJsonAsync(NativePath + "/request", new { schemaVersion = 1 });
        requested.StatusCode.Should().Be(HttpStatusCode.OK, await requested.Content.ReadAsStringAsync());
        var data = (await requested.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
        data.GetProperty("state").GetString().Should().Be("scheduled");
        data.GetProperty("scheduledAtUtc").GetDateTime().Should().BeOnOrAfter(startedAt.AddDays(30))
            .And.BeOnOrBefore(DateTime.UtcNow.AddDays(30));
        data.EnumerateObject().Select(value => value.Name).Should().BeEquivalentTo("schemaVersion", "state", "scheduledAtUtc");
        using var after = await Client.GetAsync(NativePath);
        after.Headers.CacheControl!.NoStore.Should().BeTrue();
        var saved = (await after.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
        saved.GetProperty("scheduledAtUtc").GetDateTime().Should().Be(data.GetProperty("scheduledAtUtc").GetDateTime());
        using var repeated = await Client.PostAsJsonAsync(NativePath + "/request", new { schemaVersion = 1 });
        repeated.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = (await repeated.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
        replay.GetProperty("scheduledAtUtc").GetDateTime().Should().Be(data.GetProperty("scheduledAtUtc").GetDateTime());
        _emails.Invocations.Count.Should().Be(1);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Users.SingleAsync(value => value.Id == foreign)).DeletionScheduledAt.Should().BeNull();
        (await context.Users.SingleAsync(value => value.Id == owner)).DeletionScheduledAt.Should().NotBeNull();
    }


    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.KitchenStaff)]
    public async Task Staff_cannot_read_or_request_customer_deletion(UserRole role)
    {
        await CustomerAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            CustomerBasketTokens.Create(Factory.Services, role));
        (await Client.GetAsync(NativePath)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.PostAsJsonAsync(NativePath + "/request", new { schemaVersion = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _emails.Invocations.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("malformed")]
    [InlineData("expired")]
    public async Task Missing_or_invalid_authentication_cannot_initiate_or_read_a_request(string mode)
    {
        await CustomerAsync();
        Client.DefaultRequestHeaders.Authorization = mode switch
        {
            "none" => null,
            "malformed" => new AuthenticationHeaderValue("Bearer", "invalid-token"),
            _ => new AuthenticationHeaderValue("Bearer", CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, expired: true))
        };
        (await Client.GetAsync(NativePath)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Client.PostAsJsonAsync(NativePath + "/request", new { schemaVersion = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _emails.Invocations.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"userId\":\"11111111-1111-4111-8111-111111111111\"}")]
    public async Task Invalid_or_caller_selected_identity_body_cannot_schedule_an_account(string payload)
    {
        var owner = await CustomerAsync();
        using var body = new StringContent(payload, Encoding.UTF8, "application/json");
        (await Client.PostAsync(NativePath + "/request", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Users.SingleAsync(value => value.Id == owner)).DeletionScheduledAt.Should().BeNull();
        _emails.Invocations.Count.Should().Be(0);
        (await Client.PostAsJsonAsync(NativePath + "/request", new { schemaVersion = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_customer_claim_cannot_delete_a_current_database_staff_account()
    {
        var owner = await CustomerAsync(role: UserRole.Admin);
        (await Client.GetAsync(NativePath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client.PostAsJsonAsync(NativePath + "/request", new { schemaVersion = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Users.SingleAsync(value => value.Id == owner)).DeletionScheduledAt.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_deleted_database_customer_cannot_read_or_schedule_deletion(bool softDeleted)
    {
        var owner = await CustomerAsync();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var user = await context.Users.SingleAsync(value => value.Id == owner);
            if (softDeleted) user.IsDeleted = true;
            else context.Users.Remove(user);
            await context.SaveChangesAsync();
        }
        (await Client.GetAsync(NativePath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client.PostAsJsonAsync(NativePath + "/request", new { schemaVersion = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        _emails.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_requests_keep_one_schedule_and_one_confirmation_email()
    {
        var owner = await CustomerAsync();
        var responses = await Task.WhenAll(
            Client.PostAsJsonAsync(NativePath + "/request", new { schemaVersion = 1 }),
            Client.PostAsJsonAsync(NativePath + "/request", new { schemaVersion = 1 }));
        try
        {
            responses.Select(response => response.StatusCode).Should().Contain(HttpStatusCode.OK)
                .And.OnlyContain(status => status == HttpStatusCode.OK || status == HttpStatusCode.Conflict);
            using var status = await Client.GetAsync(NativePath);
            status.StatusCode.Should().Be(HttpStatusCode.OK);
            var data = (await status.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
            await using var context = DatabaseFixture.CreateContext();
            (await context.Users.SingleAsync(user => user.Id == owner)).DeletionScheduledAt
                .Should().Be(data.GetProperty("scheduledAtUtc").GetDateTime());
            _emails.Invocations.Should().ContainSingle();
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Failed_email_preserves_the_schedule_and_never_logs_private_transport_details()
    {
        var owner = await CustomerAsync();
        const string privateDetail = "private-contact@example.test?token=fixture-private-token";
        _emails.Setup(value => value.SendAccountDeletionEmailAsync(It.IsAny<CultureInfo>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .ThrowsAsync(new Exception(privateDetail));
        (await Client.PostAsJsonAsync(NativePath + "/request", new { schemaVersion = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Users.SingleAsync(value => value.Id == owner)).DeletionScheduledAt.Should().NotBeNull();
        var logs = _logs.Invocations.Where(invocation => invocation.Method.Name == "Log").ToArray();
        logs.Should().NotBeEmpty();
        logs.Select(invocation => invocation.Arguments[2].ToString()).Should().OnlyContain(value =>
            value != null && !value.Contains(privateDetail, StringComparison.Ordinal));
        logs.Select(invocation => invocation.Arguments[3]).Should().OnlyContain(value => value == null);
    }

    private async Task<Guid> CustomerAsync(bool select = true, UserRole role = UserRole.Customer)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"native-deletion-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "Native",
            LastName = "Fixture",
            Role = role,
            CreatedBy = nameof(CustomerAccountDeletionTests),
            RefreshToken = string.Empty
        };
        var created = await users.CreateAsync(user);
        created.Succeeded.Should().BeTrue();
        if (select)
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, userId: user.Id.ToString()));
        return user.Id;
    }
}
