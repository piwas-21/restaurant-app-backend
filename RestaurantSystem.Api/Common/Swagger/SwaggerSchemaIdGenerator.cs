using System.Text;

namespace RestaurantSystem.Api.Common.Swagger;

/// <summary>
/// Builds the <c>components.schemas</c> key that Swashbuckle writes for a CLR type.
///
/// <para>
/// Why this exists: the id used to be <c>type.FullName</c>, so the published spec named the
/// order-create body
/// <c>RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand.CreateOrderCommand</c> and
/// every generic wrapper carried a full assembly-qualified argument
/// (<c>ApiResponse`1[[…OrderDto, RestaurantSystem.Api, Version=1.0.0.0, Culture=neutral,
/// PublicKeyToken=null]]</c>). Client generators derive TypeScript identifiers from those keys, so
/// the mobile app's generated types were unreadable and the client could not be regenerated
/// without renaming 178 types (mobile feedback item 2).
/// </para>
///
/// <para>
/// The naive fix — <c>type.Name</c> — does not generate: Swashbuckle refuses a schema id already
/// taken by a different type, and every closed <c>ApiResponse&lt;T&gt;</c> shares the name
/// <c>ApiResponse`1</c> (78 of them in this spec). So generic arity is spelled out instead:
/// <c>ApiResponse&lt;PagedResult&lt;OrderDto&gt;&gt;</c> becomes
/// <c>ApiResponseOfPagedResultOfOrderDto</c>.
/// </para>
///
/// <para>
/// The id is a pure function of the type, so it is stable across runs and across processes. It is
/// deliberately NOT made unique by appending a hash: two distinct types that reduce to the same id
/// must be renamed, and the failure is loud — swagger generation throws and
/// <c>SwaggerDocumentTests</c> goes red before the spec can be published.
/// </para>
/// </summary>
public static class SwaggerSchemaIdGenerator
{
    /// <summary>Separates a generic type from its arguments: <c>ApiResponseOfOrderDto</c>.</summary>
    private const string GenericArgumentSeparator = "Of";

    /// <summary>Separates the arguments of a multi-argument generic: <c>MapOfStringAndInt32</c>.</summary>
    private const string AdditionalArgumentSeparator = "And";

    /// <summary>Suffix for an array type: <c>OrderDtoArray</c>.</summary>
    private const string ArraySuffix = "Array";

    /// <summary>
    /// Prefix for a name that does not start with a letter (a compiler-generated type, say), so
    /// that every id stays a valid identifier in the languages client generators emit.
    /// </summary>
    private const string NonIdentifierPrefix = "Type";

    /// <summary>
    /// The schema id for <paramref name="type"/>. Always matches <c>^[A-Za-z][A-Za-z0-9_]*$</c>:
    /// no namespace, no assembly, no version, no backtick, bracket or comma.
    /// </summary>
    public static string Generate(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        // A nullable value type is the same schema as its underlying type — Swashbuckle expresses
        // the nullability on the property, not by minting a second schema.
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return Generate(underlying);
        }

        if (type.IsArray)
        {
            return Generate(type.GetElementType()!) + ArraySuffix;
        }

        // A nested type carries its declaring type so that Inner types of two different Outer
        // types cannot collapse onto one id.
        var prefix = type.DeclaringType is null ? string.Empty : Generate(type.DeclaringType);
        var name = prefix + Sanitize(type.Name);

        if (!type.IsGenericType)
        {
            return name;
        }

        var builder = new StringBuilder(name).Append(GenericArgumentSeparator);
        var arguments = type.GetGenericArguments();
        for (var i = 0; i < arguments.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(AdditionalArgumentSeparator);
            }

            builder.Append(Generate(arguments[i]));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Drops the CLR arity marker (<c>ApiResponse`1</c>) and anything that is not a letter, a
    /// digit or an underscore.
    /// </summary>
    private static string Sanitize(string typeName)
    {
        var arityMarker = typeName.IndexOf('`', StringComparison.Ordinal);
        var bare = arityMarker < 0 ? typeName : typeName[..arityMarker];

        var builder = new StringBuilder(bare.Length);
        foreach (var character in bare)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_');
        }

        var sanitized = builder.ToString();

        return sanitized.Length > 0 && char.IsAsciiLetter(sanitized[0])
            ? sanitized
            : NonIdentifierPrefix + sanitized;
    }
}
