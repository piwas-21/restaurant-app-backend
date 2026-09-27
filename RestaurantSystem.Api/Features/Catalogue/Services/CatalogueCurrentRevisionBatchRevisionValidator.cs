using System.Text.Json;
using System.Text;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueCurrentRevisionBatchRevisionValidator
{
    public static bool IsValid(JsonElement revision, string expectedTemplateId) =>
        revision.ValueKind == JsonValueKind.Object &&
        Encoding.UTF8.GetByteCount(revision.GetRawText()) <=
            CatalogueCurrentRevisionBatchLimits.MaximumSingleRevisionBytes &&
        revision.TryGetProperty("templateId", out var templateId) && templateId.ValueKind == JsonValueKind.String &&
        string.Equals(templateId.GetString(), expectedTemplateId, StringComparison.Ordinal) &&
        revision.TryGetProperty("revision", out var revisionValue) && revisionValue.ValueKind == JsonValueKind.Number &&
        revisionValue.TryGetInt32(out var revisionNumber) && revisionNumber > 0 &&
        revision.TryGetProperty("qualityStatus", out var quality) && quality.ValueKind == JsonValueKind.String &&
        quality.GetString() == "reviewed" &&
        revision.TryGetProperty("contentHash", out var contentHash) && contentHash.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(contentHash.GetString()) && contentHash.GetString()!.Length <= 128;
}
