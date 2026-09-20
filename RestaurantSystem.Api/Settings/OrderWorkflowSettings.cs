using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

public sealed class OrderWorkflowSettings
{
    public const string SectionName = "OrderWorkflow";

    [Range(0, 240)]
    public int DelayApprovalThresholdMinutes { get; set; } = 10;

    [Range(1, 240)]
    public int ConfirmedPreparationMinutes { get; set; } = 20;

    [Range(1, 240)]
    public int DeliveryPreparingMinutes { get; set; } = 45;
}
