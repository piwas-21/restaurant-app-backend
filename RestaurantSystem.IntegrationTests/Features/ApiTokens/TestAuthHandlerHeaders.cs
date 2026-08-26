using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Features.ApiTokens;

/// <summary>
/// The test-identity headers an API-token test has to CLEAR, named once. Without clearing them a
/// request carrying a token would also carry a fake admin identity, and every assertion here
/// would be about the harness rather than about the token.
/// </summary>
internal static class TestAuthHandlerHeaders
{
    public const string Role = TestAuthHandler.RoleHeader;
    public const string Anonymous = TestAuthHandler.AnonymousHeader;
}
