using Microsoft.AspNetCore.Authentication;

namespace RestaurantSystem.Api.Common.Authentication;

/// <summary>
/// Options for <see cref="ApiTokenAuthenticationHandler"/>. Empty today: the scheme has nothing
/// to configure — the token vocabulary lives in the database and the prefix in
/// <see cref="ApiTokenDefaults"/>. It exists because <c>AuthenticationHandler&lt;T&gt;</c> needs
/// an options type, and a dedicated one means adding a knob later is not a breaking change.
/// </summary>
public sealed class ApiTokenAuthenticationOptions : AuthenticationSchemeOptions
{
}
