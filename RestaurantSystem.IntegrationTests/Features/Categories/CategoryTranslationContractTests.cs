using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Categories.Commands.CreateCategoryCommand;
using RestaurantSystem.Api.Features.Categories.Commands.UpdateCategoryCommand;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.Categories;

[Collection("Database Lane 3")]
public sealed class CategoryTranslationContractTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CategoryTranslationsRoundTripAndOmittedUpdateFieldsArePreserved()
    {
        AuthenticateAsAdmin();
        var name = $"Category {Guid.NewGuid():N}";
        var translations = new Dictionary<string, CategoryContentDto>
        {
            ["en"] = new() { Name = name, Description = "English description" },
            ["fr"] = new() { Name = "Catégorie", Description = "Description française" }
        };
        var create = new CreateCategoryCommand(name, "English description", true, 1,
            Translations: translations,
            SourceLocale: "en",
            TranslationMetadata: new TranslationOwnerMetadataDto
            {
                SourceLocales = new Dictionary<string, string>
                {
                    ["name"] = "en",
                    ["description"] = "en"
                }
            });

        var created = await ReadResponseAsync<ApiResponse<CategoryDto>>(
            await PostAsJsonAsync("/api/categories", create));
        created!.Success.Should().BeTrue();
        created.Data!.SourceLocale.Should().Be("en");
        created.Data.Translations["fr"].Name.Should().Be("Catégorie");
        created.Data.TranslationMetadata!.SourceLocales.Should().ContainKey("name");
        created.Data.TranslationMetadata.ExpectedContentVersion.Should().HaveLength(64);

        var update = new UpdateCategoryCommand(created.Data.Id, name, "English description", false, 2);
        var updated = await ReadResponseAsync<ApiResponse<CategoryDto>>(
            await PutAsJsonAsync($"/api/categories/{created.Data.Id}", update));
        updated!.Success.Should().BeTrue();
        updated.Data!.IsActive.Should().BeFalse();
        updated.Data.SourceLocale.Should().Be("en");
        updated.Data.Translations.Should().BeEquivalentTo(translations);

        var detail = await GetFromJsonAsync<ApiResponse<CategoryDetailDto>>(
            $"/api/categories/{created.Data.Id}");
        detail!.Data!.Translations.Should().BeEquivalentTo(translations);
        detail.Data.Content.Should().BeEquivalentTo(translations);

        var clearSourceLocale = $$"""{"id":"{{created.Data.Id}}","name":"{{name}}","description":"English description","isActive":false,"sourceLocale":null}""";
        var clearedResponse = await Client.PutAsync($"/api/categories/{created.Data.Id}",
            new StringContent(clearSourceLocale, Encoding.UTF8, "application/json"));
        clearedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cleared = await ReadResponseAsync<ApiResponse<CategoryDto>>(clearedResponse);
        cleared!.Data!.SourceLocale.Should().BeNull();
        cleared.Data.Translations.Should().BeEquivalentTo(translations);
        cleared.Data.TranslationMetadata!.SourceLocales.Should().BeEmpty();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.TranslationFieldProvenances.AsNoTracking()
            .AnyAsync(row => row.EntityType == "category" && row.EntityId == created.Data.Id &&
                row.Kind == "tenantSource"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task CategoryUpdateRejectsAcceptedTextFromAStaleContentVersion()
    {
        AuthenticateAsAdmin();
        var name = $"Stale category {Guid.NewGuid():N}";
        var created = await ReadResponseAsync<ApiResponse<CategoryDto>>(
            await PostAsJsonAsync("/api/categories", new CreateCategoryCommand(name, null, true, 1)));
        var command = new UpdateCategoryCommand(created!.Data!.Id, name, null, true,
            TranslationMetadata: new TranslationOwnerMetadataDto
            {
                ExpectedContentVersion = new string('0', 64),
                AcceptedSuggestionIds = new Dictionary<string, string> { ["name.fr"] = "stale" }
            });

        var response = await PutAsJsonAsync($"/api/categories/{created.Data.Id}", command);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
