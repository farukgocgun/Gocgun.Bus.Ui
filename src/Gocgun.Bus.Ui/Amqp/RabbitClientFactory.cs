namespace Gocgun.Bus.Ui.Amqp;

internal sealed class RabbitClientFactory : IAmqpClientFactory
{
    /// <inheritdoc />
    public async Task<IAmqpPeekClient> ConnectAsync(AmqpEndpoint endpoint, CancellationToken cancellationToken)
    {
        return await RabbitPeekClient.ConnectAsync(endpoint, null, cancellationToken);
    }
}
