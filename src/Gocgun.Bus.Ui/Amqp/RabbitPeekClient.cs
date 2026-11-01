using Gocgun.Diagnostics.Shared.Tenancy;
using RabbitMQ.Client;

namespace Gocgun.Bus.Ui.Amqp;

internal sealed class RabbitPeekClient : IAmqpPeekClient
{
    private readonly IConnection connection;
    private readonly IChannel channel;
    private readonly Action<string>? probe;
    private bool disposed;

    private RabbitPeekClient(IConnection connection, IChannel channel, Action<string>? probe)
    {
        this.connection = connection;
        this.channel = channel;
        this.probe = probe;
    }

    public static async Task<RabbitPeekClient> ConnectAsync(AmqpEndpoint endpoint, Action<string>? probe, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = endpoint.Host,
            Port = endpoint.Port,
            VirtualHost = string.IsNullOrEmpty(endpoint.VirtualHost) ? "/" : endpoint.VirtualHost,
            UserName = endpoint.UserName,
            Password = endpoint.Password,
            Ssl = new SslOption { Enabled = endpoint.Tls },
        };
        var opened = await factory.CreateConnectionAsync(cancellationToken);
        var openedChannel = await opened.CreateChannelAsync(cancellationToken: cancellationToken);
        return new RabbitPeekClient(opened, openedChannel, probe);
    }

    public async Task<uint> PassiveCountAsync(string queue, CancellationToken cancellationToken)
    {
        AmqpOperationGate.Demand("queue.declare", passive: true);
        probe?.Invoke("queue.declare");
        var declared = await channel.QueueDeclareAsync(queue, false, false, false, null, true, false, cancellationToken);
        return declared.MessageCount;
    }

    public async Task<PeekedMessage?> GetAsync(string queue, CancellationToken cancellationToken)
    {
        AmqpOperationGate.Demand("basic.get");
        probe?.Invoke("basic.get");
        var got = await channel.BasicGetAsync(queue, false, cancellationToken);
        if (got is null)
        {
            return null;
        }

        string? tenant = null;
        if (got.BasicProperties.Headers is not null
            && got.BasicProperties.Headers.TryGetValue(TenantMarkers.AmqpHeaderName, out var raw))
        {
            tenant = TenantMarkers.HeaderToString(raw);
        }

        return new PeekedMessage(got.DeliveryTag, got.Redelivered, got.Body.ToArray(), tenant);
    }

    public async Task RejectRequeueAsync(ulong deliveryTag, CancellationToken cancellationToken)
    {
        AmqpOperationGate.Demand("basic.reject", requeue: true);
        probe?.Invoke("basic.reject");
        await channel.BasicRejectAsync(deliveryTag, true, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await channel.DisposeAsync();
        await connection.DisposeAsync();
    }
}
