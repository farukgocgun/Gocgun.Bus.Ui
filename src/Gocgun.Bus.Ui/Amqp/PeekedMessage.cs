namespace Gocgun.Bus.Ui.Amqp;

internal sealed record PeekedMessage(ulong DeliveryTag, bool Redelivered, byte[] Body, string? Tenant);
