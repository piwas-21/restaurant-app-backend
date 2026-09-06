using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Maintenance;

[Collection("Database Lane 1")]
public class ProductCardBackfillPagingTests(DatabaseFixture fixture) : CardBackfillTestBase(fixture)
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(301)]
    [InlineData(int.MaxValue)]
    public async Task Direct_service_rejects_limits_outside_one_through_three_hundred(int limit)
    {
        var processor = new BackfillProcessorStub((_, _) => throw new IOException("Must not process"));
        var run = () => Service(processor).RunAsync(apply: true, maxRows: limit);

        await run.Should().ThrowAsync<BadRequestException>();
        processor.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Maximum_page_is_bounded_and_cursor_reaches_the_last_row()
    {
        Context.ProductImages.AddRange(Enumerable.Range(1, 301).Select(i => new ProductImage
        {
            Id = ImageId(i),
            ProductId = ProductId,
            Url = Url($"missing-{i}.png"),
            CreatedAt = Epoch,
            CreatedBy = "backfill-test",
        }));
        await Context.SaveChangesAsync();
        var service = Service();

        var first = await service.RunAsync(apply: false, maxRows: 300);
        first.RowsScanned.Should().Be(300);
        first.SkippedMissingFile.Should().Be(300);
        first.Truncated.Should().BeTrue();
        first.NextCursor.Should().NotBeNullOrEmpty();

        var last = await service.RunAsync(apply: false, maxRows: 300, continueFrom: first.NextCursor);
        last.RowsScanned.Should().Be(1);
        last.SkippedMissingFile.Should().Be(1);
        last.Truncated.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cursor_advances_past_missing_rows_and_orders_equal_timestamps_by_id(bool apply)
    {
        // Insert backwards. The independent oracle is PostgreSQL UUID order, not insertion order
        // or an ORDER BY query reproduced by the test.
        await AddRowAsync(3, Url("third.png"));
        await AddRowAsync(2, Url("second.png"));
        await AddRowAsync(1, Url("missing.png"));
        await WriteOriginalAsync("second.png");
        await WriteOriginalAsync("third.png");
        var processor = new BackfillProcessorStub((_, _) => Task.FromResult<Stream?>(new MemoryStream(WebpBytes())));
        var service = Service(processor);

        var first = await service.RunAsync(apply, maxRows: 1);
        first.RowsScanned.Should().Be(1);
        first.SkippedMissingFile.Should().Be(1);
        first.Truncated.Should().BeTrue();
        first.NextCursor.Should().NotBeNullOrEmpty();
        processor.Calls.Should().BeEmpty();

        var second = await service.RunAsync(apply, maxRows: 1, continueFrom: first.NextCursor);
        second.RowsScanned.Should().Be(1);
        second.SkippedMissingFile.Should().Be(0);
        second.Truncated.Should().BeTrue();
        second.NextCursor.Should().NotBeNullOrEmpty().And.NotBe(first.NextCursor);

        var third = await service.RunAsync(apply, maxRows: 1, continueFrom: second.NextCursor);
        third.RowsScanned.Should().Be(1);
        third.SkippedMissingFile.Should().Be(0);
        third.Truncated.Should().BeFalse("an exact-cap final page has no following row");
        if (apply)
        {
            processor.Calls.Should().Equal("second.png", "third.png");
            (await CardUrlAsync(ImageId(2))).Should().Be(Url("second-800.webp"));
            (await CardUrlAsync(ImageId(3))).Should().Be(Url("third-800.webp"));
        }
        else
        {
            processor.Calls.Should().BeEmpty("dry runs do not decode or generate");
            Directory.GetFiles(ProductDirectory).Select(Path.GetFileName).Should().BeEquivalentTo("second.png", "third.png");
            (await CardUrlAsync(ImageId(2))).Should().BeNull();
            (await CardUrlAsync(ImageId(3))).Should().BeNull();
        }
    }

    [Fact]
    public async Task Created_at_precedes_id_and_dry_run_does_not_attach_existing_files()
    {
        await AddRowAsync(1, Url("later.png"), Epoch.AddSeconds(1));
        await AddRowAsync(9, Url("photo.png"), Epoch);
        await File.WriteAllBytesAsync(FinalPath(), WebpBytes());
        var service = Service();

        var first = await service.RunAsync(apply: false, maxRows: 1);
        first.AlreadyPresent.Should().Be(1);
        first.Truncated.Should().BeTrue();
        first.NextCursor.Should().NotBeNullOrEmpty();
        (await CardUrlAsync(ImageId(9))).Should().BeNull();
        var second = await service.RunAsync(apply: false, maxRows: 1, continueFrom: first.NextCursor);
        second.SkippedMissingFile.Should().Be(1);
        second.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task Dry_run_malformed_row_does_not_block_the_next_page()
    {
        await AddRowAsync(1, "not a URL");
        await AddRowAsync(2, Url("missing.png"));
        var service = Service();
        var first = await service.RunAsync(apply: false, maxRows: 1);
        first.RowsFailed.Should().Be(1);
        first.FailedImageIds.Should().Equal(ImageId(1));
        first.NextCursor.Should().NotBeNullOrEmpty();
        var second = await service.RunAsync(apply: false, maxRows: 1, continueFrom: first.NextCursor);
        second.RowsScanned.Should().Be(1);
        second.SkippedMissingFile.Should().Be(1);
        second.Truncated.Should().BeFalse();
    }
}
