using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Setup;
using RestaurantSystem.Api.Features.Setup.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Setup;

/// <summary>
/// The first-run setup checklist (SOFRA-ONBOARDING-PLAN O4) through the real pipeline.
///
/// The thing worth testing here is not that a checkbox round-trips — it is that the two
/// DERIVED steps cannot be faked, and that the ACKNOWLEDGED steps exist precisely
/// because provisioning has already made their data-shaped equivalents useless. Once
/// the founder is off the call, a checklist that can be told "you are done" is worse
/// than no checklist.
/// </summary>
public class SetupChecklistTests : IntegrationTestBase
{
    public SetupChecklistTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    private const string Url = "/api/admin/setup-checklist";

    private async Task<SetupChecklistDto> GetChecklistAsync()
    {
        var response = await Client.GetAsync(Url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = JsonSerializer.Deserialize<ApiResponse<SetupChecklistDto>>(
            await response.Content.ReadAsStringAsync(), JsonOptions);
        envelope!.Data.Should().NotBeNull();
        return envelope.Data!;
    }

    private static SetupStepDto Step(SetupChecklistDto checklist, string key) =>
        checklist.Steps.Single(s => s.Key == key);

    [Fact]
    public async Task Checklist_IsAdminOnly()
    {
        // It is about running the restaurant, and its step list reveals which modules
        // the tenant bought.
        AuthenticateAsAnonymous();
        (await Client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        AuthenticateAsUser();
        (await Client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        AuthenticateAsRole(UserRole.Cashier);
        (await Client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Mutations_AreAdminOnly()
    {
        // Gate everything, not just the read: a cashier who could tick steps could hide
        // the owner's remaining setup work from them.
        AuthenticateAsRole(UserRole.Cashier);

        (await Client.PutAsJsonAsync(
            $"{Url}/steps/{SetupSteps.OpeningHours}", new SetStepDoneRequest { IsDone = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.PutAsJsonAsync($"{Url}/dismissed", new SetDismissedRequest { IsDismissed = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Checklist_ReadsWithNoRow_AndReportsNothingAcknowledged()
    {
        // There is no seeded row on purpose: absent must mean "nothing done, not
        // dismissed". A seeded row would be one more thing that can be stale.
        AuthenticateAsAdmin();
        var checklist = await GetChecklistAsync();

        checklist.IsDismissed.Should().BeFalse();
        checklist.Steps.Should().NotBeEmpty();
        Step(checklist, SetupSteps.RestaurantInfo).IsDone.Should().BeFalse();
        Step(checklist, SetupSteps.OpeningHours).IsDone.Should().BeFalse();
    }

    [Fact]
    public async Task DerivedStep_CannotBeAcknowledgedByHand()
    {
        // The load-bearing assertion. `menu` is done when a menu EXISTS; accepting an
        // acknowledgement would let an owner tick off a menu they never built, and the
        // whole point of the checklist is that "nothing left to do" is earned.
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"{Url}/steps/{SetupSteps.Menu}", new SetStepDoneRequest { IsDone = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // And nothing was written — refused, not silently ignored then persisted.
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var state = await context.SetupChecklistState.FirstOrDefaultAsync();
        (state?.AcknowledgedSteps ?? []).Should().NotContain(SetupSteps.Menu);
    }

    [Fact]
    public async Task MenuStep_NeedsAProductInACategory_NotJustBothExisting()
    {
        // `SeedBasicDataAsync` produces exactly the shape that makes the naive check
        // wrong: three categories and two products, and NOT ONE `ProductCategory` row
        // between them. "Some category exists AND some product exists" reads that as a
        // finished menu — while no guest can reach a single item.
        AuthenticateAsAdmin();
        var loose = await GetChecklistAsync();
        Step(loose, SetupSteps.Menu).IsDerived.Should().BeTrue();
        Step(loose, SetupSteps.Menu).IsDone.Should().BeFalse();

        // Put one product in one category and it becomes a real menu.
        var join = await LinkFirstProductToFirstCategoryAsync();
        Step(await GetChecklistAsync(), SetupSteps.Menu).IsDone.Should().BeTrue();

        // Soft-delete the product and it stops counting. `ProductCategory` is a plain
        // `Entity`, so the join row survives its product and the global filter never
        // sees it — without the handler's own `!IsDeleted` this stays ticked forever.
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Products.Where(p => p.Id == join.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDeleted, true));
        }

        Step(await GetChecklistAsync(), SetupSteps.Menu).IsDone.Should().BeFalse();
    }

    private async Task<ProductCategory> LinkFirstProductToFirstCategoryAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var join = new ProductCategory
        {
            Id = Guid.NewGuid(),
            ProductId = (await context.Products.OrderBy(p => p.Name).FirstAsync()).Id,
            CategoryId = (await context.Categories.OrderBy(c => c.Name).FirstAsync()).Id,
            IsPrimary = true,
            DisplayOrder = 1,
            CreatedBy = "test",
            CreatedAt = DateTime.UtcNow,
        };
        context.ProductCategories.Add(join);
        await context.SaveChangesAsync();
        return join;
    }

    [Fact]
    public async Task StaffStep_CountsStaffOnly_NotRegisteredCustomers()
    {
        // The trap this exists for: every guest who registers is an ApplicationUser
        // too. Counting users would tick "invite your staff" the moment the first
        // customer signs up — congratulating an owner for work nobody did.
        AuthenticateAsAdmin();

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // One admin only — provisioning's UserSeeder shape.
            await context.Users.Where(u => u.Role != UserRole.Admin).ExecuteDeleteAsync();
            await context.Users.Where(u => u.Role == UserRole.Admin).Skip(1)
                .ExecuteDeleteAsync();
        }
        Step(await GetChecklistAsync(), SetupSteps.Staff).IsDone.Should().BeFalse();

        // A guest registering must NOT satisfy it.
        await AddUserAsync(UserRole.Customer);
        Step(await GetChecklistAsync(), SetupSteps.Staff).IsDone.Should().BeFalse();

        // A second staff member must.
        await AddUserAsync(UserRole.Server);
        Step(await GetChecklistAsync(), SetupSteps.Staff).IsDone.Should().BeTrue();
    }

    [Fact]
    public async Task StaffStep_IgnoresSoftDeletedStaff()
    {
        // ApplicationUser is the ONE soft-deletable entity excluded from the global
        // query filter (IExcludeFromGlobalFilter), so unless the handler says
        // `!IsDeleted` itself, a staff member who has since left keeps this ticked
        // forever.
        AuthenticateAsAdmin();
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Users.Where(u => u.Role != UserRole.Admin).ExecuteDeleteAsync();
            await context.Users.Where(u => u.Role == UserRole.Admin).Skip(1).ExecuteDeleteAsync();
        }

        var server = await AddUserAsync(UserRole.Server);
        Step(await GetChecklistAsync(), SetupSteps.Staff).IsDone.Should().BeTrue();

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Users.Where(u => u.Id == server)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsDeleted, true));
        }

        Step(await GetChecklistAsync(), SetupSteps.Staff).IsDone.Should().BeFalse();
    }

    [Fact]
    public async Task AcknowledgedStep_RoundTrips_AndUndoes()
    {
        AuthenticateAsAdmin();
        var key = SetupSteps.OpeningHours;

        (await Client.PutAsJsonAsync($"{Url}/steps/{key}", new SetStepDoneRequest { IsDone = true }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Step(await GetChecklistAsync(), key).IsDone.Should().BeTrue();

        // Acknowledging twice is a no-op, not an error — the UI fires this from a
        // checkbox and a retried request must land on the same answer.
        (await Client.PutAsJsonAsync($"{Url}/steps/{key}", new SetStepDoneRequest { IsDone = true }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Step(await GetChecklistAsync(), key).IsDone.Should().BeTrue();

        (await Client.PutAsJsonAsync($"{Url}/steps/{key}", new SetStepDoneRequest { IsDone = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Step(await GetChecklistAsync(), key).IsDone.Should().BeFalse();
    }

    [Fact]
    public async Task Dismissal_IsReversible_AndPreservesProgress()
    {
        // The checklist has to be RESUMABLE. An owner who hides it mid-menu on a busy
        // Friday must be able to pick it up where they left it, so dismissal hides the
        // list without touching any step.
        AuthenticateAsAdmin();
        await Client.PutAsJsonAsync(
            $"{Url}/steps/{SetupSteps.Appearance}", new SetStepDoneRequest { IsDone = true });

        await Client.PutAsJsonAsync($"{Url}/dismissed", new SetDismissedRequest { IsDismissed = true });
        var hidden = await GetChecklistAsync();
        hidden.IsDismissed.Should().BeTrue();
        Step(hidden, SetupSteps.Appearance).IsDone.Should().BeTrue();

        await Client.PutAsJsonAsync($"{Url}/dismissed", new SetDismissedRequest { IsDismissed = false });
        var restored = await GetChecklistAsync();
        restored.IsDismissed.Should().BeFalse();
        Step(restored, SetupSteps.Appearance).IsDone.Should().BeTrue();
    }

    [Fact]
    public async Task MissingBodyField_IsRefused_NotReadAsFalse()
    {
        // `IsDone` is `required`, so an absent field is a 400 rather than binding to
        // `default`. Without that, `PUT {}` means "isDone: false" — silently
        // UN-acknowledging a step the owner had ticked, on a request that said nothing
        // about it. A malformed client would quietly undo their progress.
        AuthenticateAsAdmin();
        await Client.PutAsJsonAsync(
            $"{Url}/steps/{SetupSteps.OpeningHours}", new SetStepDoneRequest { IsDone = true });
        Step(await GetChecklistAsync(), SetupSteps.OpeningHours).IsDone.Should().BeTrue();

        var response = await Client.PutAsync(
            $"{Url}/steps/{SetupSteps.OpeningHours}",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Step(await GetChecklistAsync(), SetupSteps.OpeningHours).IsDone.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownStepKey_IsRefused()
    {
        AuthenticateAsAdmin();
        var response = await Client.PutAsJsonAsync(
            $"{Url}/steps/not-a-step", new SetStepDoneRequest { IsDone = true });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<Guid> AddUserAsync(UserRole role)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = Guid.NewGuid();
        context.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = $"{role}-{id:N}@example.test",
            Email = $"{role}-{id:N}@example.test",
            NormalizedUserName = $"{role}-{id:N}@EXAMPLE.TEST".ToUpperInvariant(),
            NormalizedEmail = $"{role}-{id:N}@EXAMPLE.TEST".ToUpperInvariant(),
            FirstName = "Test",
            LastName = role.ToString(),
            Role = role,
            CreatedBy = "test",
            CreatedAt = DateTime.UtcNow,
            RefreshToken = string.Empty,
            SecurityStamp = Guid.NewGuid().ToString(),
        });
        await context.SaveChangesAsync();
        return id;
    }
}
