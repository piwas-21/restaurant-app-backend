using FluentValidation;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OrderAmendments.Commands;

public sealed record QuoteOrderAmendmentCommand(Guid OrderId, OrderAmendmentQuoteRequest Request)
    : ICommand<ApiResponse<OrderAmendmentQuoteDto>>;

public sealed class QuoteOrderAmendmentCommandValidator : AbstractValidator<QuoteOrderAmendmentCommand>
{
    public QuoteOrderAmendmentCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.Request).NotNull();
        RuleFor(command => command.Request.ExpectedOrderVersion).GreaterThan(0);
        RuleFor(command => command.Request.ExpectedAccountRevision)
            .GreaterThan(0)
            .When(command => command.Request.ExpectedAccountRevision.HasValue);
        RuleFor(command => command.Request.Reason).MaximumLength(500);
        RuleFor(command => command.Request.ProviderConsentNote).MaximumLength(500);
        RuleFor(command => command.Request.PointsToRedeem)
            .GreaterThanOrEqualTo(0)
            .When(command => command.Request.PointsToRedeem.HasValue);
        RuleFor(command => command.Request)
            .Must(request => (request.Additions?.Count ?? 0) + (request.Changes?.Count ?? 0) is > 0 and <= 200)
            .WithMessage("An amendment must contain between one and 200 additions or source-line changes.");
        RuleForEach(command => command.Request.Additions).ChildRules(item =>
        {
            item.RuleFor(line => line.Quantity).GreaterThan(0);
            item.RuleFor(line => line)
                .Must(line => InstructionsFit(line))
                .WithMessage("Item instructions cannot exceed 500 characters.");
            item.RuleFor(line => line)
                .Must(line => line.ProductId.HasValue ^ line.MenuId.HasValue)
                .WithMessage("Each added item must reference exactly one product or menu.");
        });
        RuleForEach(command => command.Request.Changes).ChildRules(change =>
        {
            change.RuleFor(line => line.OrderItemId).NotEmpty();
            change.RuleFor(line => line.Kind).IsInEnum();
            change.RuleFor(line => line)
            .Must(line => line.Current is null || InstructionsFit(line.Current))
            .WithMessage("Item instructions cannot exceed 500 characters.");
            change.RuleFor(line => line)
            .Must(line => line.Kind == OrderAmendmentChangeKind.InstructionChange
                ? line.StartOrdinal == 0 && line.Quantity == 0 && line.Current is not null
                : line.StartOrdinal > 0 && line.Quantity > 0
                        && (line.Kind == OrderAmendmentChangeKind.Replace
                            ? line.Current is not null : line.Current is null))
                .WithMessage("Use a one-based quantity range for void/replace, or a whole-line snapshot for instruction changes.");
        });
    }

    private static bool InstructionsFit(CreateOrderItemDto item) =>
        (item.SpecialInstructions?.Length ?? 0) <= 500
        && (item.ChildItems ?? []).All(InstructionsFit);
}

public sealed class QuoteOrderAmendmentCommandHandler
    : ICommandHandler<QuoteOrderAmendmentCommand, ApiResponse<OrderAmendmentQuoteDto>>
{
    private readonly IOrderAmendmentQuoteService _quotes;

    public QuoteOrderAmendmentCommandHandler(IOrderAmendmentQuoteService quotes) => _quotes = quotes;

    public async Task<ApiResponse<OrderAmendmentQuoteDto>> Handle(
        QuoteOrderAmendmentCommand command, CancellationToken cancellationToken)
    {
        var quote = await _quotes.QuoteAsync(command.OrderId, command.Request, cancellationToken);
        return ApiResponse<OrderAmendmentQuoteDto>.SuccessWithData(quote, "Order amendment quote prepared");
    }
}
