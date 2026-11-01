using Gocgun.Bus.Ui.Amqp;
using Gocgun.Diagnostics.Shared.Decoding;

namespace Gocgun.Bus.Ui;

internal sealed class BusSession
{
    internal AmqpEndpoint? Endpoint { get; set; }

    internal ContractCatalog Catalog { get; set; } = ContractCatalog.Empty;

    internal string Profile { get; set; } = "session";
}
