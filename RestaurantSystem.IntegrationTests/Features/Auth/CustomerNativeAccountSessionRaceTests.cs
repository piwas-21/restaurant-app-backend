using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class CustomerNativeAccountSessionRaceTests(DatabaseFixture fixture) : CustomerNativeAccountFixture(fixture)
{
    private readonly IssuancePause _pause = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddScoped<RefreshSessionService>();
        services.RemoveAll<IRefreshSessionService>();
        services.AddScoped<IRefreshSessionService>(provider => new PausedSessions(provider.GetRequiredService<RefreshSessionService>(), _pause));
    }

    [Fact]
    public async Task A_login_that_validated_the_old_password_cannot_issue_after_reset_revokes_the_account()
    {
        var user = await CustomerAsync(confirmed: true);
        var proof = await SendLinkAsync(user, "recover");
        _pause.Armed = true;
        var login = Client.PostAsJsonAsync("/api/Auth/login", new { email = user.Email, password = Password });
        try
        {
            await _pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await SuccessAsync(await PostAsync("reset-password", new { schemaVersion = 1, proof, newPassword = NewPassword, confirmPassword = NewPassword }));
        }
        finally { _pause.Release.TrySetResult(); }
        var stale = await login.WaitAsync(TimeSpan.FromSeconds(20));
        stale.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await stale.Content.ReadAsStringAsync()).Should().NotContain("accessToken").And.NotContain("refreshToken");
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.RefreshSessions.CountAsync(row => row.UserId == user.Id && row.RevokedAt == null)).Should().Be(0);
        var fresh = await Client.PostAsJsonAsync("/api/Auth/login", new { email = user.Email, password = NewPassword });
        fresh.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fresh.Content.ReadAsStringAsync()).Should().Contain("accessToken").And.Contain("refreshToken");
        (await context.RefreshSessions.CountAsync(row => row.UserId == user.Id && row.RevokedAt == null)).Should().Be(1);
    }

    [Fact]
    public async Task Preloaded_refresh_rows_cannot_replay_an_already_rotated_credential()
    {
        var user = await CustomerAsync(confirmed: true);
        string refresh;
        using (var issue = Factory.Services.CreateScope())
            refresh = await issue.ServiceProvider.GetRequiredService<IRefreshSessionService>().IssueAsync(user, default);
        using var stale = Factory.Services.CreateScope();
        var oldUser = (await stale.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(user.Id.ToString()))!;
        var staleContext = stale.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await staleContext.RefreshSessions.SingleAsync(row => row.UserId == user.Id)).RevokedAt.Should().BeNull();
        using (var rotate = Factory.Services.CreateScope())
            (await rotate.ServiceProvider.GetRequiredService<IRefreshSessionService>().RotateAsync(user, refresh, default)).Should().NotBeNull();
        (await stale.ServiceProvider.GetRequiredService<IRefreshSessionService>().RotateAsync(oldUser, refresh, default)).Should().BeNull();
    }

    private sealed class IssuancePause
    {
        public bool Armed { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PausedSessions(IRefreshSessionService inner, IssuancePause pause) : IRefreshSessionService
    {
        public async Task<string> IssueAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            if (pause.Armed)
            {
                pause.Armed = false;
                pause.Entered.TrySetResult();
                await pause.Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return await inner.IssueAsync(user, cancellationToken);
        }
        public Task<string?> RotateAsync(ApplicationUser user, string refreshToken, CancellationToken cancellationToken) => inner.RotateAsync(user, refreshToken, cancellationToken);
        public Task RevokeAllAsync(ApplicationUser user, CancellationToken cancellationToken) => inner.RevokeAllAsync(user, cancellationToken);
    }
}
