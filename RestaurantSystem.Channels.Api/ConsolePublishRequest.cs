using System.Text.Json.Serialization;

namespace RestaurantSystem.Channels.Api;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConsolePublishRequest(string Revision = "");
