using System.Globalization;

namespace Gocgun.Bus.Ui.Amqp;

internal sealed class AmqpEndpoint
{
    internal AmqpEndpoint(string host, int port, string virtualHost, string userName, string password, bool tls)
    {
        Host = host;
        Port = port;
        VirtualHost = virtualHost;
        UserName = userName;
        Password = password;
        Tls = tls;
    }

    internal string Host { get; }

    internal int Port { get; }

    internal string VirtualHost { get; }

    internal string UserName { get; }

    internal string Password { get; }

    internal bool Tls { get; }

    public override string ToString()
    {
        return Host + ":" + Port.ToString(CultureInfo.InvariantCulture) + VirtualHost;
    }
}
