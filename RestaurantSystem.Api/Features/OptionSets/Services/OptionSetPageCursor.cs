using System.Text;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

internal static class OptionSetPageCursor
{
    public static string Encode(Guid id) => Convert.ToBase64String(Encoding.UTF8.GetBytes(id.ToString("D")));

    public static Guid Decode(string cursor)
    {
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            if (!Guid.TryParse(decoded, out var id))
            {
                throw new FormatException();
            }

            return id;
        }
        catch (FormatException)
        {
            throw new BadRequestException("The option-set cursor is invalid");
        }
    }
}
