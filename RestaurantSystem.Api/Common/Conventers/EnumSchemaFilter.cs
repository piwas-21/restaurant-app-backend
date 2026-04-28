using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;

namespace RestaurantSystem.Api.Common.Conventers;

/// <summary>
/// Swagger schema filter to show enums as strings with EnumMember values in documentation
/// </summary>
public class EnumSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type.IsEnum && schema is OpenApiSchema openApiSchema)
        {
            openApiSchema.Enum?.Clear();
            openApiSchema.Type = JsonSchemaType.String;
            openApiSchema.Format = null;

            var enumValues = new List<JsonNode>();

            foreach (var enumValue in Enum.GetValues(context.Type))
            {
                var enumMember = context.Type.GetMember(enumValue.ToString()!).FirstOrDefault();
                var enumMemberAttribute = enumMember?.GetCustomAttributes(typeof(EnumMemberAttribute), false)
                    .Cast<EnumMemberAttribute>().FirstOrDefault();

                var value = enumMemberAttribute?.Value ?? enumValue.ToString()!;
                enumValues.Add(JsonValue.Create(value));
            }

            openApiSchema.Enum = enumValues;
        }
    }
}
