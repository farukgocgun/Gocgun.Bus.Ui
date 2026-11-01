namespace Gocgun.Bus.Ui.Amqp;

internal interface IAmqpClientFactory
{
    Task<IAmqpPeekClient> ConnectAsync(AmqpEndpoint endpoint, CancellationToken cancellationToken);
}
