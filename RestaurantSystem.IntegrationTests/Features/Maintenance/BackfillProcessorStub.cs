using Microsoft.AspNetCore.Http;
using RestaurantSystem.Api.Common.Services.Interfaces;

namespace RestaurantSystem.IntegrationTests.Features.Maintenance;

internal sealed class BackfillProcessorStub(
    Func<string, CancellationToken, Task<Stream?>> generate) : IImageProcessor
{
    public List<string> Calls { get; } = [];

    public Task<Stream?> GenerateCardVariantAsync(Stream source, string fileName, int maxEdge,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(fileName);
        return generate(fileName, cancellationToken);
    }

    public Task<Stream?> ProcessAsync(IFormFile file, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The backfill must call GenerateCardVariantAsync.");

    public Task<Stream?> ProcessAsync(Stream source, string fileName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The backfill must call GenerateCardVariantAsync.");
}

/// <summary>Fails AFTER bytes reach the destination, not before a writer has opened it.</summary>
internal sealed class PartialCardStream(byte[] bytes, Func<CancellationToken, Task> afterPartialWrite)
    : MemoryStream((byte[])bytes.Clone())
{
    public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        const int partialLength = 16;
        await destination.WriteAsync(bytes.AsMemory(0, partialLength), cancellationToken);
        await destination.FlushAsync(cancellationToken);
        await afterPartialWrite(cancellationToken);
        await destination.WriteAsync(bytes.AsMemory(partialLength), cancellationToken);
    }
}
