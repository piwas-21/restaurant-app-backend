using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using DeleteCommand = RestaurantSystem.Api.Features.GlobalIngredients.Commands.DeleteGlobalIngredientCommand.DeleteGlobalIngredientCommand;

namespace RestaurantSystem.IntegrationTests.Features.GlobalIngredients;

/// <summary>
/// The delete had to be a SOFT delete and was a hard one, silently.
/// </summary>
/// <remarks>
/// The handler called <c>Remove()</c> under a comment reading "soft delete handled by entity type
/// configuration". It is not: the Deleted → <c>IsDeleted</c> conversion lives in
/// <c>ApplicationDbContext.ApplyAuditInformation</c>, which is called only from the SYNCHRONOUS
/// <c>SaveChanges()</c> override — and every handler in this codebase calls
/// <c>SaveChangesAsync</c>. So the row was permanently deleted, together with its translations.
/// <para>
/// Every other delete command sets the flag by hand, which is precisely why this was the only one
/// affected: those manual assignments ARE the workaround for the same hole. The hole itself
/// (overriding <c>SaveChangesAsync</c>) is a live-system behaviour change and is tracked separately.
/// </para>
/// <para>
/// The assertion that matters is the one under <c>IgnoreQueryFilters()</c>: without it, "the row is
/// gone from the filtered set" is equally true of a soft delete and a hard one, and the test would
/// pass against the bug it exists to catch.
/// </para>
/// </remarks>
public class DeleteGlobalIngredientSoftDeleteTests : IntegrationTestBase
{
    private Guid _ingredientId;

    public DeleteGlobalIngredientSoftDeleteTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Deleting_hides_the_ingredient_but_KEEPS_the_row()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
            var result = await mediator.SendCommand<DeleteCommand, ApiResponse<string>>(new DeleteCommand(_ingredientId));
            result.Success.Should().BeTrue(result.Message);
        }

        using var check = Factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await context.GlobalIngredients.AnyAsync(g => g.Id == _ingredientId))
            .Should().BeFalse("a deleted ingredient must not appear in any ordinary query");

        // soft-delete-bypass: the whole point is to prove the row SURVIVED. Without this the
        // assertion above is satisfied by the hard delete this test exists to catch.
        var stored = await context.GlobalIngredients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == _ingredientId);

        stored.Should().NotBeNull("Remove() + SaveChangesAsync deletes the row outright — that was the bug");
        stored!.IsDeleted.Should().BeTrue();
        stored.DeletedAt.Should().NotBeNull();
        stored.DeletedBy.Should().NotBeNullOrEmpty("a delete is an audited action");
    }

    /// <summary>
    /// A hard delete took the translations with it. They are the reason this mattered beyond
    /// tidiness: a product ingredient linked to a global one resolves its localized names through
    /// them, so the rows are not recoverable from the product side.
    /// </summary>
    [Fact]
    public async Task Deleting_keeps_the_translations_too()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
            await mediator.SendCommand<DeleteCommand, ApiResponse<string>>(new DeleteCommand(_ingredientId));
        }

        using var check = Factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var translations = await context.GlobalIngredientTranslations
            .IgnoreQueryFilters()
            .Where(t => t.GlobalIngredientId == _ingredientId)
            .ToListAsync();

        translations.Should().HaveCount(2);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var ingredient = new GlobalIngredient
        {
            DefaultName = "§9.18 Sumac",
            CreatedBy = "test",
            Translations =
            {
                new GlobalIngredientTranslation { LanguageCode = "en", Name = "Sumac", CreatedBy = "test" },
                new GlobalIngredientTranslation { LanguageCode = "tr", Name = "Sumak", CreatedBy = "test" }
            }
        };

        context.Add(ingredient);
        await context.SaveChangesAsync();
        _ingredientId = ingredient.Id;
    }
}
