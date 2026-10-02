using System.Text.Json.Serialization;

namespace RestaurantSystem.Channels.Api;

public sealed record ConsoleLoginRequest(string AccessKey);
public sealed record ConsoleEnableRequest([property: JsonRequired] bool Enable);
public sealed record ConsoleDecisionRequest(string Action, string Reason, [property: JsonRequired] bool ReviewedInstructions);
