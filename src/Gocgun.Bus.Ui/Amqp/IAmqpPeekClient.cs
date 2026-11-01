namespace Gocgun.Bus.Ui.Amqp;

internal interface IAmqpPeekClient : IAsyncDisposable
{
    Task<uint> PassiveCountAsync(string queue, CancellationToken cancellationToken);

    Task<PeekedMessage?> GetAsync(string queue, CancellationToken cancellationToken);

    Task RejectRequeueAsync(ulong deliveryTag, CancellationToken cancellationToken);
}
