namespace Notification.Infrastructure.Services.Email;

using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Notification.Application.Interfaces;
using Notification.Application.Services.Email;

public sealed class EmailBackgroundQueue : IEmailBackgroundQueue
{
    private readonly Channel<SendOtpEmailDto> _queue;

    public EmailBackgroundQueue(int capacity = 2000)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<SendOtpEmailDto>(options);
    }

    public ValueTask QueueOtpEmailAsync(SendOtpEmailDto dto, CancellationToken cancellationToken = default)
    {
        return _queue.Writer.WriteAsync(dto, cancellationToken);
    }

    public ValueTask<SendOtpEmailDto> DequeueOtpEmailAsync(CancellationToken cancellationToken)
    {
        return _queue.Reader.ReadAsync(cancellationToken);
    }
}
