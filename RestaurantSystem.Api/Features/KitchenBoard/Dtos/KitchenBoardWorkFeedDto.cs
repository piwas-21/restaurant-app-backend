namespace RestaurantSystem.Api.Features.KitchenBoard.Dtos;

public sealed record KitchenBoardWorkFeedDto(
    KitchenBoardPageDto<KitchenBoardOrderDto> Orders,
    KitchenBoardPageDto<KitchenBoardCorrectionDto> Corrections,
    KitchenBoardPageDto<KitchenBoardCompletionDto> Completions);
