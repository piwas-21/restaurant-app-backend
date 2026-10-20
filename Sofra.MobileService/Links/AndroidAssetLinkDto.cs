using System.Text.Json.Serialization;

namespace Sofra.MobileService.Links;

public sealed record AndroidAssetLinkDto(IReadOnlyList<string> Relation, AndroidAssetTargetDto Target);
public sealed record AndroidAssetTargetDto(
    [property: JsonPropertyName("namespace")] string Namespace,
    [property: JsonPropertyName("package_name")] string PackageName,
    [property: JsonPropertyName("sha256_cert_fingerprints")] IReadOnlyList<string> Fingerprints);
