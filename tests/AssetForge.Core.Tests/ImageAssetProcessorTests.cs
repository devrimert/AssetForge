using AssetForge.Core.Contracts;
using AssetForge.Core.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetForge.Core.Tests;

public sealed class ImageAssetProcessorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "assetforge-tests", Guid.NewGuid().ToString("N"));

    public ImageAssetProcessorTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ImageAssetProcessor CreateProcessor() =>
        new(new FileSystemAssetCache(), NullLogger<ImageAssetProcessor>.Instance);

    private async Task<string> CreateSourceImageAsync(int size = 256)
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.png");
        using var image = new Image<Rgba32>(size, size);
        await image.SaveAsPngAsync(path);
        return path;
    }

    private AssetJob NewJob(string sourcePath, ProcessingOptions? options = null) => new()
    {
        Id = Guid.NewGuid(),
        SourcePath = sourcePath,
        OutputDirectory = Path.Combine(_root, "out"),
        Options = options ?? new ProcessingOptions()
    };

    [Fact]
    public async Task Generates_a_mip_chain_down_to_the_minimum_size()
    {
        var source = await CreateSourceImageAsync(size: 256);
        var job = NewJob(source);

        var result = await CreateProcessor().ProcessAsync(job, CancellationToken.None);

        // 256, 128, 64, 32, 16, 8, 4 -> seven levels
        Assert.Equal(7, result.MipLevels);
        Assert.False(result.FromCache);
        Assert.Equal(7, Directory.GetFiles(result.OutputPath!, "mip_*.png").Length);
    }

    [Fact]
    public async Task Identical_input_is_served_from_the_cache()
    {
        var source = await CreateSourceImageAsync();
        var processor = CreateProcessor();

        var first = await processor.ProcessAsync(NewJob(source), CancellationToken.None);
        var second = await processor.ProcessAsync(NewJob(source), CancellationToken.None);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);

        // Same content and options -> same entry, no duplicated work on disk.
        Assert.Equal(first.OutputPath, second.OutputPath);
        Assert.Equal(first.MipLevels, second.MipLevels);
    }

    [Fact]
    public async Task Different_options_produce_a_different_cache_entry()
    {
        var source = await CreateSourceImageAsync();
        var processor = CreateProcessor();

        var wide = await processor.ProcessAsync(
            NewJob(source, new ProcessingOptions { MinMipSize = 4 }), CancellationToken.None);

        var shallow = await processor.ProcessAsync(
            NewJob(source, new ProcessingOptions { MinMipSize = 64 }), CancellationToken.None);

        Assert.NotEqual(wide.OutputPath, shallow.OutputPath);
        Assert.False(shallow.FromCache);
        Assert.True(shallow.MipLevels < wide.MipLevels);
    }

    [Fact]
    public async Task A_hanging_asset_honours_cancellation()
    {
        var source = await CreateSourceImageAsync();
        var job = NewJob(source, new ProcessingOptions { SimulateHang = true });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await CreateProcessor().ProcessAsync(job, cts.Token));
    }
}
