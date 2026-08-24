using FluentAssertions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Swagger;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using System.Text.RegularExpressions;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// Unit-level rules for the swagger schema-id generator (mobile feedback item 2).
///
/// <para>
/// <c>SwaggerDocumentTests</c> proves the whole published spec is clean; this file pins the SHAPE
/// of each rule, so a regression says which rule broke instead of only that some key somewhere is
/// wrong.
/// </para>
/// </summary>
public class SwaggerSchemaIdGeneratorTests
{
    /// <summary>The identifier shape a client generator (kubb, NSwag, openapi-generator) can use verbatim.</summary>
    private static readonly Regex Identifier = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.None, TimeSpan.FromSeconds(1));

    [Fact]
    public void NonGenericType_UsesShortNameOnly()
    {
        SwaggerSchemaIdGenerator.Generate(typeof(OrderDto)).Should().Be("OrderDto");
        SwaggerSchemaIdGenerator.Generate(typeof(CreateOrderCommand)).Should().Be("CreateOrderCommand");
    }

    [Fact]
    public void GenericType_SpellsOutItsArgument()
    {
        SwaggerSchemaIdGenerator.Generate(typeof(ApiResponse<OrderDto>)).Should().Be("ApiResponseOfOrderDto");
    }

    [Fact]
    public void NestedGeneric_RecursesLeftToRight()
    {
        SwaggerSchemaIdGenerator.Generate(typeof(ApiResponse<PagedResult<OrderDto>>))
            .Should().Be("ApiResponseOfPagedResultOfOrderDto");
    }

    [Fact]
    public void GenericOverPrimitive_UsesTheClrTypeName()
    {
        SwaggerSchemaIdGenerator.Generate(typeof(ApiResponse<bool>)).Should().Be("ApiResponseOfBoolean");
        SwaggerSchemaIdGenerator.Generate(typeof(ApiResponse<string>)).Should().Be("ApiResponseOfString");
    }

    [Fact]
    public void GenericOverCollection_KeepsTheCollectionInTheName()
    {
        SwaggerSchemaIdGenerator.Generate(typeof(ApiResponse<List<OrderDto>>))
            .Should().Be("ApiResponseOfListOfOrderDto");
    }

    [Fact]
    public void MultiArgumentGeneric_JoinsArgumentsWithAnd()
    {
        SwaggerSchemaIdGenerator.Generate(typeof(Dictionary<string, int>))
            .Should().Be("DictionaryOfStringAndInt32");
    }

    [Fact]
    public void NullableValueType_CollapsesOntoItsUnderlyingType()
    {
        SwaggerSchemaIdGenerator.Generate(typeof(int?))
            .Should().Be(SwaggerSchemaIdGenerator.Generate(typeof(int)));
    }

    [Fact]
    public void ArrayType_GetsAnArraySuffix()
    {
        SwaggerSchemaIdGenerator.Generate(typeof(OrderDto[])).Should().Be("OrderDtoArray");
        SwaggerSchemaIdGenerator.Generate(typeof(OrderDto[][])).Should().Be("OrderDtoArrayArray");
    }

    /// <summary>
    /// Two <c>Inner</c> types under two different outer types must not collapse onto one id — the
    /// declaring type is part of the name.
    /// </summary>
    [Fact]
    public void NestedType_CarriesItsDeclaringType()
    {
        SwaggerSchemaIdGenerator.Generate(typeof(FirstOuter.Inner)).Should().Be("FirstOuterInner");
        SwaggerSchemaIdGenerator.Generate(typeof(SecondOuter.Inner)).Should().Be("SecondOuterInner");
    }

    [Fact]
    public void EveryGeneratedId_IsAPlainIdentifier()
    {
        var types = new[]
        {
            typeof(OrderDto),
            typeof(CreateOrderCommand),
            typeof(ApiResponse<OrderDto>),
            typeof(ApiResponse<PagedResult<OrderDto>>),
            typeof(ApiResponse<List<OrderDto>>),
            typeof(Dictionary<string, int>),
            typeof(OrderDto[]),
            typeof(FirstOuter.Inner),
            typeof(int?)
        };

        foreach (var type in types)
        {
            var id = SwaggerSchemaIdGenerator.Generate(type);
            Identifier.IsMatch(id).Should().BeTrue($"'{id}' must be usable as a TypeScript identifier");
            id.Should().NotContain(".").And.NotContain("`").And.NotContain("[").And.NotContain("+");
        }
    }

    [Fact]
    public void Generation_IsStable()
    {
        SwaggerSchemaIdGenerator.Generate(typeof(ApiResponse<PagedResult<OrderDto>>))
            .Should().Be(SwaggerSchemaIdGenerator.Generate(typeof(ApiResponse<PagedResult<OrderDto>>)));
    }

    [Fact]
    public void NullType_IsRejected()
    {
        var act = () => SwaggerSchemaIdGenerator.Generate(null!);

        act.Should().Throw<ArgumentNullException>();
    }

}

/// <summary>Top-level on purpose: a type nested in the test class would prefix every id with the test class name.</summary>
internal sealed class FirstOuter
{
    internal sealed class Inner;
}

/// <summary>The second half of the "two Inner types must not collide" fixture.</summary>
internal sealed class SecondOuter
{
    internal sealed class Inner;
}
