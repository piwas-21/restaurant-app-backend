namespace RestaurantSystem.Channels.Api;

public sealed record ConsoleLoginRequest(string AccessKey);
public sealed record ConsoleEnableRequest(bool Enable);
public sealed record ConsoleDecisionRequest(string Action, string Reason, bool ReviewedInstructions);
