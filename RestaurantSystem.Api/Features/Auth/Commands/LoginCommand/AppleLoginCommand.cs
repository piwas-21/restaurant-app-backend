using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Dtos;

namespace RestaurantSystem.Api.Features.Auth.Commands.LoginCommand;

public class AppleLoginCommand : ICommand<ApiResponse<AuthResponse>>
{
    public string IdToken { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
