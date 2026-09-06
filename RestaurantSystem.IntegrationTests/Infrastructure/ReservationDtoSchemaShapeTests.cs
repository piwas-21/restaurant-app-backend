using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// The reservation write DTOs share a base record (#420 follow-up: they were one declaration
/// written twice, and Sonar measured a 41-line duplicated block between them). Inheritance is
/// only an acceptable way to say that if the PUBLISHED CONTRACT does not move — the mobile
/// client is generated from this document with kubb, so an <c>allOf</c>/<c>$ref</c> where there
/// used to be a flat object is a breaking change to a consumer that does not live in this
/// repository, and nothing else here would notice.
/// </summary>
[Collection("Database Lane 1")]
public class ReservationDtoSchemaShapeTests
{
    private readonly DatabaseFixture _databaseFixture;

    public ReservationDtoSchemaShapeTests(DatabaseFixture databaseFixture) => _databaseFixture = databaseFixture;

    private static readonly string[] SharedProperties =
    [
        "customerName", "customerEmail", "customerPhone", "tableId",
        "reservationDate", "startTime", "endTime", "numberOfGuests", "specialRequests",
    ];

    private Microsoft.OpenApi.OpenApiDocument Document()
    {
        using var factory = new TestWebApplicationFactory(
            _databaseFixture.ConnectionString, disableApplicationHostedServices: true);
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
    }

    [Theory]
    [InlineData("CreateReservationDto")]
    [InlineData("UpdateReservationDto")]
    public void The_write_dto_still_publishes_every_shared_field_directly(string id)
    {
        var schemas = Document().Components!.Schemas!;

        schemas.Should().ContainKey(id);
        var schema = schemas[id];

        // Inherited properties must be INLINE. Swashbuckle flattens by default; if
        // `UseAllOfForInheritance` is ever switched on, this is what says so before a
        // regenerated mobile client does.
        schema.AllOf.Should().BeNullOrEmpty($"{id} must stay a flat object for the kubb-generated client");
        schema.Properties!.Keys.Should().Contain(SharedProperties);
    }

    /// <summary>
    /// The control: the two schemas are NOT the same object. An update carries two fields a
    /// booking cannot set, and a refactor that accidentally collapsed them into one type would
    /// satisfy every assertion above.
    /// </summary>
    [Fact]
    public void Only_the_update_dto_carries_the_edit_only_fields()
    {
        var schemas = Document().Components!.Schemas!;

        schemas["UpdateReservationDto"].Properties!.Keys.Should().Contain(new[] { "status", "notes" });
        schemas["CreateReservationDto"].Properties!.Keys.Should().NotContain(new[] { "status", "notes" });
    }

    /// <summary>
    /// The base itself must not surface as its own schema — an abstract type nothing posts would
    /// be a phantom model in every generated client.
    /// </summary>
    [Fact]
    public void The_shared_base_is_not_published_as_a_model()
    {
        Document().Components!.Schemas!.Should().NotContainKey("ReservationWriteDto");
    }

    /// <summary>
    /// #561: the booking request carries the combined-tables extension. Additive and create-only —
    /// the update DTO deliberately has no way to change a combined set, and this pins BOTH halves
    /// so the next field cannot silently land on the wrong one.
    /// </summary>
    [Fact]
    public void Only_the_create_dto_carries_the_combined_tables_extension()
    {
        var schemas = Document().Components!.Schemas!;

        schemas["CreateReservationDto"].Properties!.Keys.Should().Contain("combinedTableIds");
        schemas["UpdateReservationDto"].Properties!.Keys.Should().NotContain("combinedTableIds");
        schemas["ReservationDto"].Properties!.Keys.Should().Contain("combinedTableIds");
    }
}
