using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public sealed record OrderAmendmentResolutionStartOutcomeDto(
    string Outcome,
    OrderAmendmentResolutionResultDto? Result,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    OrderAmendmentResolutionRefusalDto? Refusal)
{
    public static OrderAmendmentResolutionStartOutcomeDto Accepted(
        OrderAmendmentResolutionResultDto result) => new("accepted", result, null);

    public static OrderAmendmentResolutionStartOutcomeDto Refused(
        OrderAmendmentResolutionRefusalDto refusal) => new("refused", null, refusal);
}

public sealed record OrderAmendmentResolutionRefusalDto(
    Guid ActorUserId,
    Guid OrderId,
    Guid AmendmentId,
    Guid ClientOperationId,
    string RequestHash,
    string FailureCode,
    DateTime CreatedAt,
    OrderAmendmentResolutionStartRequest OriginalRequest);
