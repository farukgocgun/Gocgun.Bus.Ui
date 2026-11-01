namespace Gocgun.Bus.Ui.Amqp;

internal static class QueuePeek
{
    internal static async Task<PeekedMessage?> RunAsync(IAmqpPeekClient client, string queue, CancellationToken cancellationToken)
    {
        AmqpOperationGate.Demand("basic.get");
        var message = await client.GetAsync(queue, cancellationToken);
        if (message is null)
        {
            return null;
        }

        try
        {
            AmqpOperationGate.Demand("basic.reject", requeue: true);
            await client.RejectRequeueAsync(message.DeliveryTag, cancellationToken);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }

        return message;
    }
}
