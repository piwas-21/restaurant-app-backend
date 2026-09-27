using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OptionSets.Dtos;

public sealed class OptionSetDetailDto
{
    public Guid Id { get; set; }
    public OptionSetKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceLocale { get; set; } = "en";
    public Dictionary<string, string> Translations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public OptionSetStatus Status { get; set; }
    public int Version { get; set; }
    public List<OptionSetEntryDto> Entries { get; set; } = [];
    public List<OptionSetAttachmentDto> Attachments { get; set; } = [];
    public string? SourceTemplateId { get; set; }
    public int? SourceRevision { get; set; }
}
