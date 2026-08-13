using System.Collections.Concurrent;
using AssetForge.Core.Contracts;
using AssetForge.Core.Queues;

namespace AssetForge.Core.Tests;

public class ChannelJobQueueTests
{
    private static AssetJob NewJob() => new()
    {
        Id = Guid.NewGuid(),
        SourcePath = @"C:\in\texture.png",
        OutputDirectory = @"C:\out"
    };

    [Fact]
    public async Task Dequeue_returns_the_job_that_was_enqueued()
    {
        var queue = new ChannelJobQueue(capacity: 4);
        var job = NewJob();

        await queue.EnqueueAsync(job);
        var dequeued = await queue.DequeueAsync();

        // Records compare by value, so this checks every field at once.
        Assert.Equal(job, dequeued);
    }

    [Fact]
    public async Task Enqueue_waits_while_the_queue_is_full()
    {
        var queue = new ChannelJobQueue(capacity: 1);
        await queue.EnqueueAsync(NewJob());

        // The queue is full, so this must not complete yet.
        var pending = queue.EnqueueAsync(NewJob()).AsTask();
        await Task.Delay(200);
        Assert.False(pending.IsCompleted);

        // Freeing a slot lets the waiting producer through.
        await queue.DequeueAsync();
        await pending.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Dequeue_throws_when_the_token_is_cancelled()
    {
        var queue = new ChannelJobQueue();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await queue.DequeueAsync(cts.Token));
    }

    [Fact]
    public async Task ReadAll_drains_the_queue_and_then_ends()
    {
        var queue = new ChannelJobQueue();
        await queue.EnqueueAsync(NewJob());
        await queue.EnqueueAsync(NewJob());
        queue.CompleteAdding();

        var received = 0;
        await foreach (var _ in queue.ReadAllAsync())
        {
            received++;
        }

        Assert.Equal(2, received);
    }

    [Fact]
    public async Task Every_job_is_delivered_exactly_once_under_concurrency()
    {
        const int producers = 4;
        const int jobsPerProducer = 250;
        const int consumers = 8;

        var queue = new ChannelJobQueue(capacity: 32);
        var received = new ConcurrentBag<Guid>();

        var consumerTasks = Enumerable.Range(0, consumers)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var job in queue.ReadAllAsync())
                {
                    received.Add(job.Id);
                }
            }))
            .ToArray();

        var producerTasks = Enumerable.Range(0, producers)
            .Select(_ => Task.Run(async () =>
            {
                for (var i = 0; i < jobsPerProducer; i++)
                {
                    await queue.EnqueueAsync(NewJob());
                }
            }))
            .ToArray();

        await Task.WhenAll(producerTasks);
        queue.CompleteAdding();
        await Task.WhenAll(consumerTasks);

        Assert.Equal(producers * jobsPerProducer, received.Count);
        Assert.Equal(producers * jobsPerProducer, received.Distinct().Count());
    }
}