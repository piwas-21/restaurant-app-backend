using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OptionSets.Dtos;

public sealed class OptionSetSummaryDto
{
    public Guid Id { get; set; }
    public OptionSetKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceLocale { get; set; } = "en";
    public OptionSetStatus Status { get; set; }
    public int Version { get; set; }
    public int EntryCount { get; set; }
    public int AttachmentCount { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? SourceTemplateId { get; set; }
    public int? SourceRevision { get; set; }
}
