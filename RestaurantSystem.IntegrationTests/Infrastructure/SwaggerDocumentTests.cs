using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Swagger;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// The published v1 swagger document must stay machine-consumable (mobile feedback item 2).
///
/// <para>
/// The mobile client is generated from <c>/api/swagger/v1/swagger.json</c> with kubb, which turns
/// every <c>components.schemas</c> key into a TypeScript identifier. While the key was
/// <c>type.FullName</c>, those identifiers were things like
/// <c>RestaurantSystemApiFeaturesOrdersCommandsCreateOrderCommandCreateOrderCommand</c> and the
/// client could not be regenerated without a 178-type rename. This test is the guard: it builds
/// the real document in-process and fails if any key stops being a plain identifier, or if two
/// different CLR types ever claim the same key.
/// </para>
///
/// <para>
/// It boots its own host because it wraps <c>SchemaGeneratorOptions.SchemaIdSelector</c> to record
/// which CLR type asked for which id — the finished document is a dictionary and can therefore no
/// longer show a duplicate, only the exception Swashbuckle would have thrown on the way there.
/// </para>
/// </summary>
[Collection("Database Lane 1")]
public class SwaggerDocumentTests
{
    /// <summary>What a client generator can use verbatim: no dot, backtick, bracket, comma or space.</summary>
    private static readonly Regex Identifier = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private readonly DatabaseFixture _databaseFixture;

    public SwaggerDocumentTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture ?? throw new ArgumentNullException(nameof(databaseFixture));
    }

    [Fact]
    public void SwaggerDocument_GeneratesWithReadableUniqueSchemaIds()
    {
        var (document, idsByType) = GenerateDocument();

        document.Components.Should().NotBeNull();
        var schemaIds = document.Components!.Schemas!.Keys.ToList();
        schemaIds.Should().HaveCountGreaterThan(100, "the whole API surface is documented");

        // 1. every id is a plain identifier a code generator can emit as-is
        foreach (var id in schemaIds)
        {
            Identifier.IsMatch(id).Should().BeTrue($"schema id '{id}' must be a plain identifier");
        }

        // 2. no id leaks the namespace, the assembly or its version
        schemaIds.Should().OnlyContain(id => !id.Contains("RestaurantSystem", StringComparison.Ordinal));
        schemaIds.Should().OnlyContain(id => !id.Contains("Version=", StringComparison.Ordinal));

        // 3. no two distinct CLR types claim one id. Swashbuckle would have thrown, but only for a
        //    type pair that both reach the document on the same run; asserting it here names the
        //    clash instead of leaving a nested "Failed to generate schema for type" chain.
        var clashes = idsByType
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .Where(group => group.Select(pair => pair.Key).Distinct().Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(" | ", group.Select(pair => pair.Key.FullName))}")
            .ToList();

        clashes.Should().BeEmpty("two types sharing a schema id must be renamed, never hash-suffixed");
    }

    /// <summary>
    /// The two names quoted in the mobile feedback, pinned so a future change to the generator
    /// cannot silently rename them under the client again.
    /// </summary>
    [Fact]
    public void SwaggerDocument_ExposesTheDocumentedNames()
    {
        var (document, _) = GenerateDocument();
        var schemaIds = document.Components!.Schemas!.Keys.ToList();

        schemaIds.Should().Contain("CreateOrderCommand");
        schemaIds.Should().Contain("ApiResponseOfOrderDto");
        schemaIds.Should().Contain("ApiResponseOfPagedResultOfOrderDto");

        // and the document really is using the generator under test
        schemaIds.Should().Contain(SwaggerSchemaIdGenerator.Generate(typeof(CreateOrderCommand)));
        schemaIds.Should().Contain(SwaggerSchemaIdGenerator.Generate(typeof(ApiResponse<OrderDto>)));
    }

    private (Microsoft.OpenApi.OpenApiDocument Document, IReadOnlyList<KeyValuePair<Type, string>> IdsByType) GenerateDocument()
    {
        var recorded = new ConcurrentBag<KeyValuePair<Type, string>>();

        using var factory = new TestWebApplicationFactory(
            _databaseFixture.ConnectionString,
            configureTestServices: services => services.PostConfigure<SchemaGeneratorOptions>(options =>
            {
                var configured = options.SchemaIdSelector;
                options.SchemaIdSelector = type =>
                {
                    var id = configured(type);
                    recorded.Add(new KeyValuePair<Type, string>(type, id));
                    return id;
                };
            }),
            disableApplicationHostedServices: true);

        using var scope = factory.Services.CreateScope();
        var swaggerProvider = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>();

        // Throws if any two types collide on an id, so "it generated at all" is itself an assertion.
        var document = swaggerProvider.GetSwagger("v1");

        return (document, recorded.ToList());
    }
}
