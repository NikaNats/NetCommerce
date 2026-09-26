#nullable enable
using System.Threading.Channels;

namespace NetCommerce.Finance.Infrastructure.Services;

/// <summary>
///     Lock-free bounded channel that decouples PII access auditing from reads.
///     Read queries enqueue the accessed profile id without blocking; a
///     background flusher batches the <c>last_accessed_at</c> updates.
///     Bounded with <see cref="BoundedChannelFullMode.DropOldest"/> so audit
///     traffic can never exert backpressure on customer-facing reads.
/// </summary>
public sealed class PiiAccessAuditChannel
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(50_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public void RecordAccess(Guid profileId) => _channel.Writer.TryWrite(profileId);

    public bool TryRead(out Guid profileId) => _channel.Reader.TryRead(out profileId);
}
