using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text.Json.Nodes;

namespace RestaurantSystem.IntegrationTests.Features.Products;

[Collection("Database Lane 3")]
public partial class CustomerStepManifestContractTests : IntegrationTestBase
{
    public CustomerStepManifestContractTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    [Fact]
    public async Task AuthoredOrder_RoundTripsStableReferencesRolesAndRevision()
    {
        AuthenticateAsAdmin();
        var manifest = Manifest(0, VariationStep(0), SideStep(1));
        var response = await PutAsync(manifest);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await ReadManifestAsync();
        saved!["revision"]!.GetValue<int>().Should().Be(1);
        saved["steps"]!.AsArray().Should().HaveCount(2);
        saved["steps"]![0]!["targetId"]!.GetValue<Guid>().Should().Be(_variationId);
        saved["steps"]![1]!["targetId"]!.GetValue<Guid>().Should().Be(_sideAssociationId);
        saved["steps"]![1]!["compositionRole"]!.GetValue<string>().Should().Be("Drink");

        var reordered = Manifest(1, SideStep(0), VariationStep(1));
        (await PutAsync(reordered)).StatusCode.Should().Be(HttpStatusCode.OK);
        var readback = await ReadManifestAsync();
        readback!["revision"]!.GetValue<int>().Should().Be(2);
        readback["steps"]![0]!["kind"]!.GetValue<string>().Should().Be("ProductSuggestedSide");
    }

    [Fact]
    public async Task OmittedManifest_PreservesSettingsAndStillUpdatesTheProduct()
    {
        AuthenticateAsAdmin();
        (await PutAsync(Manifest(0, VariationStep(0), SideStep(1)))).StatusCode.Should().Be(HttpStatusCode.OK);
        var before = await ReadManifestAsync();
        (await PutAsync(null, includeManifest: false, name: "Renamed without screen changes"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.DeepEquals(before, await ReadManifestAsync()).Should().BeTrue();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Products.SingleAsync(row => row.Id == _productId)).Name
            .Should().Be("Renamed without screen changes", "a rename proves the omitted-field update reached its write path");
    }

    [Fact]
    public async Task ExplicitNull_IsRejectedWhileEmptyStepsResetsWithANewRevision()
    {
        AuthenticateAsAdmin();
        (await PutAsync(Manifest(0, VariationStep(0)))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutAsync(null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadManifestAsync())!["revision"]!.GetValue<int>().Should().Be(1);
        (await PutAsync(Manifest(1))).StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await ReadManifestAsync();
        saved!["revision"]!.GetValue<int>().Should().Be(2);
        saved["steps"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task StaleSave_ReturnsConflictWithCurrentRevisionAndDoesNotOverwrite()
    {
        AuthenticateAsAdmin();
        (await PutAsync(Manifest(0, VariationStep(0), SideStep(1)))).StatusCode.Should().Be(HttpStatusCode.OK);
        var before = await ReadManifestAsync();
        var stale = await PutAsync(Manifest(0, SideStep(0)), name: "Stale editor name");
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = JsonNode.Parse(await stale.Content.ReadAsStringAsync())!;
        body["data"]!["currentRevision"]!.GetValue<int>().Should().Be(1);
        JsonNode.DeepEquals(before, await ReadManifestAsync()).Should().BeTrue();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Products.SingleAsync(row => row.Id == _productId)).Name.Should().Be("Screen order product");
    }

    [Theory]
    [InlineData("foreignVariation")]
    [InlineData("sideProductInsteadOfAssociation")]
    [InlineData("duplicateReference")]
    [InlineData("invalidRole")]
    [InlineData("unknownRole")]
    [InlineData("labelOnDrink")]
    [InlineData("duplicateScreenOrder")]
    [InlineData("unsupportedSchema")]
    [InlineData("negativeOrder")]
    public async Task InvalidManifest_IsRejectedWithoutPersisting(string defect)
    {
        AuthenticateAsAdmin();
        var variation = VariationStep(0);
        var side = SideStep(1);
        var manifest = Manifest(0, variation, side);
        switch (defect)
        {
            case "foreignVariation": variation["targetId"] = _foreignVariationId; break;
            case "sideProductInsteadOfAssociation": side["targetId"] = _sideProductId; break;
            case "duplicateReference": manifest["steps"]!.AsArray().Add(variation.DeepClone()); break;
            case "invalidRole": variation["compositionRole"] = "drink"; break;
            case "unknownRole": side["compositionRole"] = "unknown"; break;
            case "labelOnDrink": side["presentationLabel"] = "Soft drink"; break;
            case "duplicateScreenOrder": side["presentationOrder"] = 0; break;
            case "unsupportedSchema": manifest["schemaVersion"] = 2; break;
            case "negativeOrder": variation["presentationOrder"] = -1; break;
        }
        (await PutAsync(manifest)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadManifestAsync()).Should().BeNull();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var product = await context.Products.SingleAsync(row => row.Id == _productId);
        product.CustomerStepManifestRevision.Should().Be(0);
        product.Name.Should().Be("Seeded screen order product", "an invalid authoring write is atomic");
    }

    [Fact]
    public async Task TwoDatabaseEditors_RevisionTokenRejectsLostUpdates()
    {
        using var firstScope = Factory.Services.CreateScope();
        using var secondScope = Factory.Services.CreateScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var first = await firstContext.Products.SingleAsync(row => row.Id == _productId);
        var second = await secondContext.Products.SingleAsync(row => row.Id == _productId);
        first.CustomerStepManifestRevision = 1;
        first.CustomerStepManifestJson = Manifest(1, VariationStep(0)).ToJsonString();
        await firstContext.SaveChangesAsync();
        second.Name = "Concurrent legacy name edit";
        Func<Task> save = async () => await secondContext.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        using var readScope = Factory.Services.CreateScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await readContext.Products.SingleAsync(row => row.Id == _productId);
        saved.CustomerStepManifestRevision.Should().Be(1);
        saved.Name.Should().Be("Seeded screen order product");
    }
}
