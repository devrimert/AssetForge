using System.Threading.Channels;
using AssetForge.Core.Contracts;

namespace AssetForge.Core.Queues
{
    /// <summary>
    /// In-process <see cref="IJobQueue"/> backed by a bounded <see cref="Channel{T}"/>.
    /// </summary>
    public sealed class ChannelJobQueue : IJobQueue
    {
        private readonly Channel<AssetJob> _channel;
        
        /// <param name="capacity">Maximum number of queued jobs.</param>
        public ChannelJobQueue(int capacity = 100)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
            var options = new BoundedChannelOptions(capacity)
            {
                // Wait for a free slot instead of dropping or throwing.
                FullMode = BoundedChannelFullMode.Wait,

                // Many workers read, the API and the supervisor write.
                SingleReader = false,
                SingleWriter = false,

                // Never run a waiting consumer's continuatuon on the producer's thread: that would let one job's work block an unrelated caller.
                AllowSynchronousContinuations = false
            };
            _channel = Channel.CreateBounded<AssetJob>(options);
        }
        public ValueTask EnqueueAsync(AssetJob job, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(job);
            return _channel.Writer.WriteAsync(job, cancellationToken);
        }

        public ValueTask<AssetJob> DequeueAsync(CancellationToken cancellationToken = default) => _channel.Reader.ReadAsync(cancellationToken);

        public IAsyncEnumerable<AssetJob> ReadAllAsync(CancellationToken cancellationToken = default) => _channel.Reader.ReadAllAsync(cancellationToken);

        public ValueTask<int> GetDepthAsync(CancellationToken cancellationToken = default) => new ValueTask<int>(_channel.Reader.Count);

        public void CompleteAdding() => _channel.Writer.Complete();
    }
}
