namespace Gocgun.Bus.Ui.Amqp;

internal static class AmqpOperationGate
{
    internal static void Demand(string operation, bool requeue = true, bool passive = true)
    {
        if (operation.Equals("basic.get", StringComparison.Ordinal))
        {
            return;
        }

        if (operation.Equals("basic.reject", StringComparison.Ordinal) && requeue)
        {
            return;
        }

        if (operation.Equals("queue.declare", StringComparison.Ordinal) && passive)
        {
            return;
        }

        throw new InvalidOperationException("AMQP operation is not allowlisted.");
    }
}
