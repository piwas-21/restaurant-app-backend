using System.Globalization;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Interfaces;

public interface ICustomerNativeAccountMail
{
    Task SendAsync(CultureInfo culture, ApplicationUser user, string token, string action);
}
