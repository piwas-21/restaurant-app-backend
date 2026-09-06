using FluentAssertions;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Maintenance;

[Collection("Database Lane 1")]
public class ProductCardBackfillPathTests(DatabaseFixture fixture) : CardBackfillTestBase(fixture)
{
    [Theory]
    [InlineData("https://images.example.test/media/tenant-a", "D")]
    [InlineData("https://images.example.test/uploads/archive/uploads", "N")]
    [InlineData("http://images.example.test:8080/custom-prefix/", "D")]
    public async Task Configured_prefix_is_removed_once_and_guid_formats_are_accepted(string baseUrl, string format)
    {
        await AddRowAsync(1, Url("photo.png", baseUrl, format));
        await WriteOriginalAsync(productFormat: format);

        var report = await Service(baseUrl: baseUrl).RunAsync(apply: true, maxRows: 1);

        report.RowsScanned.Should().Be(1);
        report.VariantsCreated.Should().Be(1);
        report.RowsFailed.Should().Be(0);
        report.Truncated.Should().BeFalse();
        (await CardUrlAsync(ImageId(1))).Should().Be(Url("photo-800.webp", baseUrl, format));
        await AssertValidCardAsync(FinalPath(productFormat: format));
    }

    [Theory]
    [InlineData("not a URL")]
    [InlineData("products/{product}/photo.png")]
    [InlineData("https://foreign.example.test/uploads/products/{product}/photo.png")]
    [InlineData("https://images.example.test.evil.test/uploads/products/{product}/photo.png")]
    [InlineData("https://images.example.test:444/uploads/products/{product}/photo.png")]
    [InlineData("ftp://images.example.test/uploads/products/{product}/photo.png")]
    [InlineData("https://images.example.test/uploads-extra/products/{product}/photo.png")]
    [InlineData("https://images.example.test/uploads/products/99999999-9999-9999-9999-999999999999/photo.png")]
    [InlineData("https://images.example.test/uploads/products/not-a-guid/photo.png")]
    [InlineData("https://images.example.test/uploads/products/{product}/../{product}/photo.png")]
    [InlineData("https://images.example.test/uploads/products/{product}/%2e%2e/{product}/photo.png")]
    [InlineData("https://images.example.test/uploads/products/{product}/sub/photo.png")]
    [InlineData("https://images.example.test/uploads/products/{product}/%2fphoto.png")]
    [InlineData("https://images.example.test/uploads/products/{product}/%5cphoto.png")]
    public async Task Unsafe_url_is_failed_without_reading_a_plausible_local_file(string hostileUrl)
    {
        await AddRowAsync(1, hostileUrl.Replace("{product}", ProductId.ToString(), StringComparison.Ordinal));
        await WriteOriginalAsync();
        // A valid existing final is bait too: validation of the URL must precede reuse.
        var sentinel = WebpBytes();
        await File.WriteAllBytesAsync(FinalPath(), sentinel);
        var processor = new BackfillProcessorStub((_, _) => throw new IOException("Unsafe path reached processor"));

        var report = await Service(processor).RunAsync(apply: true, maxRows: 1);

        report.RowsScanned.Should().Be(1);
        report.RowsFailed.Should().Be(1);
        report.FailedImageIds.Should().Equal(ImageId(1));
        report.AlreadyPresent.Should().Be(0);
        report.VariantsCreated.Should().Be(0);
        report.Truncated.Should().BeFalse();
        processor.Calls.Should().BeEmpty();
        (await CardUrlAsync(ImageId(1))).Should().BeNull();
        (await File.ReadAllBytesAsync(FinalPath())).Should().Equal(sentinel);
    }

    [Theory]
    [InlineData("uploads")]
    [InlineData("products")]
    [InlineData("product")]
    [InlineData("source")]
    [InlineData("target")]
    public async Task Symlink_at_any_component_cannot_read_reuse_or_overwrite_an_external_sentinel(string component)
    {
        await AddRowAsync(1, Url("photo.png"));
        var external = Path.Combine(ContentRoot, "external");
        Directory.CreateDirectory(external);
        var sentinelPath = Path.Combine(external, "sentinel.webp");
        var sentinel = WebpBytes();
        await File.WriteAllBytesAsync(sentinelPath, sentinel);

        if (component is "source" or "target")
        {
            await WriteOriginalAsync();
            var link = component == "source" ? Path.Combine(ProductDirectory, "photo.png") : FinalPath();
            if (File.Exists(link))
            {
                File.Delete(link);
            }
            File.CreateSymbolicLink(link, sentinelPath);
        }
        else
        {
            var link = component switch
            {
                "uploads" => UploadsRoot,
                "products" => Path.Combine(UploadsRoot, "products"),
                _ => ProductDirectory,
            };
            Directory.Delete(link, recursive: true);
            Directory.CreateSymbolicLink(link, external);
            // Populate through the link so both final and original would exist if followed.
            Directory.CreateDirectory(ProductDirectory);
            await File.WriteAllBytesAsync(FinalPath(), sentinel);
            await WriteOriginalAsync();
        }
        var externalFiles = Directory.GetFiles(external, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        var processor = new BackfillProcessorStub((_, _) => throw new IOException("Symlink reached processor"));

        var report = await Service(processor).RunAsync(apply: true, maxRows: 1);

        report.RowsFailed.Should().Be(1);
        report.FailedImageIds.Should().Equal(ImageId(1));
        report.AlreadyPresent.Should().Be(0);
        processor.Calls.Should().BeEmpty();
        (await CardUrlAsync(ImageId(1))).Should().BeNull();
        Directory.GetFiles(external, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(externalFiles.Keys);
        foreach (var (path, bytes) in externalFiles)
        {
            (await File.ReadAllBytesAsync(path)).Should().Equal(bytes, "no file outside uploads may change");
        }
    }
}
