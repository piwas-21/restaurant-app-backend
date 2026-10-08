using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.KitchenBoard.Dtos;
using RestaurantSystem.Api.Features.KitchenBoard.Queries.GetKitchenBoardWorkQuery;
using RestaurantSystem.Api.Features.KitchenBoard.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.KitchenBoard;

public sealed class GetKitchenBoardWorkQueryHandlerTests
{
    [Theory]
    [InlineData(75, 100, 75)]
    [InlineData(250, 100, 100)]
    public async Task Omitted_page_size_uses_configured_default_clamped_to_maximum(
        int configuredDefault, int maximum, int expectedPageSize)
    {
        var reader = new Mock<IKitchenBoardWorkReader>(MockBehavior.Strict);
        var query = new GetKitchenBoardWorkQuery(
            PageSize: null,
            OrdersCursor: "orders-watermark",
            CorrectionsCursor: "corrections-watermark",
            CompletionsCursor: "completions-watermark");
        reader.Setup(value => value.ReadAsync(
                query, expectedPageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyFeed());
        var handler = CreateHandler(reader.Object, configuredDefault, maximum);

        var response = await handler.Handle(query, CancellationToken.None);

        response.Success.Should().BeTrue();
        reader.Verify(value => value.ReadAsync(
            query, expectedPageSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Explicit_page_size_overrides_the_configured_default()
    {
        var reader = new Mock<IKitchenBoardWorkReader>(MockBehavior.Strict);
        var query = new GetKitchenBoardWorkQuery(PageSize: 17);
        reader.Setup(value => value.ReadAsync(
                query, 17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyFeed());
        var handler = CreateHandler(reader.Object, configuredDefault: 4, maximum: 30);

        var response = await handler.Handle(query, CancellationToken.None);

        response.Success.Should().BeTrue();
        reader.Verify(value => value.ReadAsync(query, 17, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    public async Task Explicit_page_sizes_outside_the_configured_range_are_rejected(int requestedPageSize)
    {
        var reader = new Mock<IKitchenBoardWorkReader>(MockBehavior.Strict);
        var handler = CreateHandler(reader.Object, configuredDefault: 8, maximum: 30);
        var query = new GetKitchenBoardWorkQuery(PageSize: requestedPageSize);

        Func<Task> act = () => handler.Handle(query, CancellationToken.None);
        var exception = await act.Should().ThrowAsync<BadRequestException>();

        exception.Which.Message.Should().Contain("between 1 and 30");
        reader.VerifyNoOtherCalls();
    }

    private static GetKitchenBoardWorkQueryHandler CreateHandler(
        IKitchenBoardWorkReader reader, int configuredDefault, int maximum)
    {
        var features = new Mock<ITenantFeatures>(MockBehavior.Strict);
        features.SetupGet(value => value.TableAccountV1).Returns(true);
        return new GetKitchenBoardWorkQueryHandler(
            reader,
            Options.Create(new OperationalQueueSyncOptions
            {
                DefaultPageSize = configuredDefault,
                MaxPageSize = maximum,
            }),
            features.Object);
    }

    private static KitchenBoardWorkFeedDto EmptyFeed()
    {
        var emptyIds = Array.Empty<Guid>();
        return new KitchenBoardWorkFeedDto(
            new KitchenBoardPageDto<KitchenBoardOrderDto>(
                Array.Empty<KitchenBoardOrderDto>(), 0, false, emptyIds, null, 0, "Watermark"),
            new KitchenBoardPageDto<KitchenBoardCorrectionDto>(
                Array.Empty<KitchenBoardCorrectionDto>(), 0, false, emptyIds, null, 0, "Watermark"),
            new KitchenBoardPageDto<KitchenBoardCompletionDto>(
                Array.Empty<KitchenBoardCompletionDto>(), 0, false, emptyIds, null, 0, "Watermark"));
    }
}
