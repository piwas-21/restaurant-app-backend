using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

internal sealed record MenuAuthoringSearchCursor(string Name, int TypeRank, Guid Id)
{
    private const string InvalidCursorMessage = "The authoring search cursor is invalid";

    public static string Encode(MenuAuthoringSearchCandidateDto candidate) =>
        WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new MenuAuthoringSearchCursor(
            candidate.Name,
            MenuAuthoringCandidateTypes.Rank(candidate.Type),
            candidate.Id)));

    public static MenuAuthoringSearchCursor Decode(string value, int maximumLength)
    {
        if (value.Length > maximumLength)
        {
            throw new BadRequestException(InvalidCursorMessage);
        }

        try
        {
            return JsonSerializer.Deserialize<MenuAuthoringSearchCursor>(WebEncoders.Base64UrlDecode(value))
                is { Id: var id, TypeRank: >= 0 and <= 4 } cursor
                && id != Guid.Empty
                ? cursor
                : throw new BadRequestException(InvalidCursorMessage);
        }
        catch (FormatException)
        {
            throw new BadRequestException(InvalidCursorMessage);
        }
        catch (JsonException)
        {
            throw new BadRequestException(InvalidCursorMessage);
        }
    }
}
