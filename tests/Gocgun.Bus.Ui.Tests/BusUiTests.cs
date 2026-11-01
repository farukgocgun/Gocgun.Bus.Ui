using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Gocgun.Bus.Ui.Amqp;
using Gocgun.Diagnostics.Fixture;
using Gocgun.Diagnostics.Shared.Bus.V1;
using Gocgun.Diagnostics.Shared.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Gocgun.Bus.Ui.Tests;

public sealed class BusUiTests
{
    [Fact]
    public void Gate_RejectsPublishPurgeAndRejectWithoutRequeue()
    {
        AmqpOperationGate.Demand("basic.get");
        AmqpOperationGate.Demand("basic.reject", requeue: true);
        AmqpOperationGate.Demand("queue.declare", passive: true);
        Assert.Throws<InvalidOperationException>(() => AmqpOperationGate.Demand("basic.publish"));
        Assert.Throws<InvalidOperationException>(() => AmqpOperationGate.Demand("queue.purge"));
        Assert.Throws<InvalidOperationException>(() => AmqpOperationGate.Demand("queue.delete"));
        Assert.Throws<InvalidOperationException>(() => AmqpOperationGate.Demand("basic.ack"));
        Assert.Throws<InvalidOperationException>(() => AmqpOperationGate.Demand("basic.nack"));
        Assert.Throws<InvalidOperationException>(() => AmqpOperationGate.Demand("basic.reject", requeue: false));
        Assert.Throws<InvalidOperationException>(() => AmqpOperationGate.Demand("queue.declare", passive: false));
        var names = typeof(IAmqpPeekClient).GetMethods().Select(method => method.Name);
        Assert.DoesNotContain(names, name => name.Contains("Publish", StringComparison.OrdinalIgnoreCase) || name.Contains("Purge", StringComparison.OrdinalIgnoreCase) || name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Peek_Requeues_AndDoesNotPublish()
    {
        var client = new FakeClient();
        var message = await QueuePeek.RunAsync(client, "orders.dlq", CancellationToken.None);
        Assert.NotNull(message);
        Assert.Equal(1, client.Gets);
        Assert.Equal(1, client.Rejects);
        Assert.Empty(client.Publishes);
    }

    [Fact]
    public async Task Web_PastePeekLoginAndAudit()
    {
        var fake = new FakeClient();
        await using var factory = new AppFactory { Client = fake };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var setupToken = factory.Services.GetRequiredService<SetupToken>().Value;
        var wrongToken = await client.PostAsync("/Setup", Form(await Token(client, "/Setup"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Token"] = "not-the-token", ["Password"] = "correct-horse-battery", ["Confirm"] = "correct-horse-battery" }));
        Assert.Contains("Setup token is invalid.", await wrongToken.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var token = await Token(client, "/Setup");
        var setup = await client.PostAsync("/Setup", Form(token, new Dictionary<string, string>(StringComparer.Ordinal) { ["Token"] = setupToken, ["Password"] = "correct-horse-battery", ["Confirm"] = "correct-horse-battery" }));
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);

        using var stranger = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        foreach (var path in new[] { "/", "/Paste", "/Peek", "/Queue" })
        {
            var blocked = await stranger.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
            Assert.Contains("/Login", blocked.Headers.Location?.OriginalString, StringComparison.Ordinal);
        }

        var csrf = await stranger.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/Login")
        {
            Content = Form(null, new Dictionary<string, string>(StringComparer.Ordinal) { ["Password"] = "correct-horse-battery" }),
        });
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);

        var person = new PersonCache { DisplayName = "Ada", KeyId = "key-9", EmailCipher = Encoding.UTF8.GetBytes("alice@example.com") };
        var envelope = new Envelope
        {
            MessageId = "m1",
            Payload = new EnvelopePayload { TypeUrl = "type.googleapis.com/gocgun.diagnostics.fixture.v1.PersonCache", Value = Bytes(person) },
            SentAt = DateTime.UnixEpoch.AddSeconds(5),
        };
        var body = Convert.ToBase64String(Bytes(envelope));
        var paste = await client.PostAsync("/Paste", Form(await Token(client, "/Paste"), new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Body"] = body,
            ["HeaderText"] = "tenant-id=tenant-a",
            ["Tenant"] = "tenant-b",
        }));
        var pasteHtml = await paste.Content.ReadAsStringAsync();
        Assert.Contains("tenant filter hid the payload", pasteHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("alice@example.com", pasteHtml, StringComparison.Ordinal);

        var shown = await client.PostAsync("/Paste", Form(await Token(client, "/Paste"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Body"] = body }));
        var shownHtml = await shown.Content.ReadAsStringAsync();
        Assert.Contains("m1", shownHtml, StringComparison.Ordinal);
        var bad = await client.PostAsync("/Paste", Form(await Token(client, "/Paste"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Body"] = "!!!!" }));
        Assert.Contains("not base64", await bad.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var unconfirmed = await client.PostAsync("/Peek", Form(await Token(client, "/Peek"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Queue"] = "orders", ["requeue"] = "false" }));
        Assert.Contains("Confirm", await unconfirmed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, fake.Gets);

        var connect = await client.PostAsync("/", Form(await Token(client, "/"), new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Host"] = "127.0.0.1",
            ["Port"] = "5672",
            ["VirtualHost"] = "/",
            ["UserName"] = "guest",
            ["Password"] = "AMQPPASSWORD",
            ["ProfileName"] = "session",
        }));
        Assert.Equal(HttpStatusCode.Redirect, connect.StatusCode);
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains("AMQPPASSWORD", StringComparison.Ordinal));

        var peek = await client.PostAsync("/Peek", Form(await Token(client, "/Peek"), new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Queue"] = "orders.dlq",
            ["ConfirmLive"] = "yes",
            ["requeue"] = "false",
        }));
        Assert.Equal(HttpStatusCode.OK, peek.StatusCode);
        Assert.Equal(1, fake.Gets);
        Assert.Equal(1, fake.Rejects);
        var audit = await File.ReadAllTextAsync(Path.Combine(factory.DataDir, "audit.log"));
        Assert.Contains("orders.dlq", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("PAYLOADSECRET", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("AMQPPASSWORD", audit, StringComparison.Ordinal);
        Assert.Contains("AGPL-3.0-only", await File.ReadAllTextAsync(FindUp("Gocgun.Bus.Ui.csproj")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Web_LogoutRevokesCookie_ContractRootAndLoginLockout()
    {
        await using var factory = new AppFactory { Client = new FakeClient() };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var setupToken = factory.Services.GetRequiredService<SetupToken>().Value;
        var setup = await client.PostAsync("/Setup", Form(await Token(client, "/Setup"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Token"] = setupToken, ["Password"] = "correct-horse-battery", ["Confirm"] = "correct-horse-battery" }));
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);
        var cookie = setup.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]).First(value => value.StartsWith("gocgun-bus-ui.auth=", StringComparison.Ordinal));
        Assert.DoesNotContain("expires=", string.Join(";", setup.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);

        var refused = await client.PostAsync("/Paste", Form(await Token(client, "/Paste"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Body"] = "AA==", ["Folder"] = "/tmp" }));
        Assert.Contains("Contract loading is disabled", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var logout = await client.PostAsync("/Logout", Form(await Token(client, "/Paste"), new Dictionary<string, string>(StringComparer.Ordinal)));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Contains("/Login", logout.Headers.Location?.OriginalString, StringComparison.Ordinal);

        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        var replay = new HttpRequestMessage(HttpMethod.Get, "/Paste");
        replay.Headers.TryAddWithoutValidation("Cookie", cookie);
        var replayed = await other.SendAsync(replay);
        Assert.Equal(HttpStatusCode.Redirect, replayed.StatusCode);
        Assert.Contains("/Login", replayed.Headers.Location?.OriginalString, StringComparison.Ordinal);
        var audit = await File.ReadAllTextAsync(Path.Combine(factory.DataDir, "audit.log"));
        Assert.Contains("\"action\":\"logout\"", audit, StringComparison.Ordinal);
        Assert.Contains("contract-load-refused", audit, StringComparison.Ordinal);

        using var attacker = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        for (var i = 0; i < DiagnosticsOptions.DefaultLoginMaxFailures; i++)
        {
            await attacker.PostAsync("/Login", Form(await Token(attacker, "/Login"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Password"] = "wrong-" + i }));
        }

        var locked = await attacker.PostAsync("/Login", Form(await Token(attacker, "/Login"), new Dictionary<string, string>(StringComparer.Ordinal) { ["Password"] = "correct-horse-battery" }));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task Rabbit_PeekLeavesRedeliveredMessage()
    {
        await using var container = new RabbitMqBuilder("rabbitmq:3.13-alpine").Build();
        await container.StartAsync();
        var uri = new Uri(container.GetConnectionString());
        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var vhost = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : Uri.UnescapeDataString(uri.AbsolutePath);
        var endpoint = new AmqpEndpoint(uri.Host, uri.Port, vhost, user, password, uri.Scheme == Uri.UriSchemeHttps);
        var queue = "peek-" + Guid.NewGuid().ToString("N");
        var body = Encoding.UTF8.GetBytes("peek-body");
        var seedFactory = new ConnectionFactory { Uri = uri };
        await using var seedConnection = await seedFactory.CreateConnectionAsync();
        await using var seedChannel = await seedConnection.CreateChannelAsync();
        await seedChannel.QueueDeclareAsync(queue, false, false, true, null, false, false, CancellationToken.None);
        await seedChannel.BasicPublishAsync(string.Empty, queue, false, new BasicProperties(), body, CancellationToken.None);

        var calls = new List<string>();
        await using var client = await RabbitPeekClient.ConnectAsync(endpoint, calls.Add, CancellationToken.None);
        var peeked = await QueuePeek.RunAsync(client, queue, CancellationToken.None);
        Assert.NotNull(peeked);
        Assert.Equal(body, peeked.Body);
        Assert.Contains("basic.get", calls, StringComparer.Ordinal);
        Assert.Contains("basic.reject", calls, StringComparer.Ordinal);
        Assert.DoesNotContain(calls, call => call.Contains("publish", StringComparison.OrdinalIgnoreCase));

        var again = await seedChannel.BasicGetAsync(queue, false, CancellationToken.None);
        Assert.NotNull(again);
        Assert.True(again.Redelivered);
        Assert.Equal(body, again.Body.ToArray());
        await seedChannel.BasicAckAsync(again.DeliveryTag, false, CancellationToken.None);
    }

    private static byte[] Bytes<T>(T value)
    {
        using var stream = new MemoryStream();
        ProtoBuf.Serializer.Serialize(stream, value);
        return stream.ToArray();
    }

    private static FormUrlEncodedContent Form(string? token, Dictionary<string, string> fields)
    {
        if (token is not null)
        {
            fields["__RequestVerificationToken"] = token;
        }

        return new FormUrlEncodedContent(fields);
    }

    private static async Task<string> Token(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success)
        {
            match = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"", RegexOptions.None, TimeSpan.FromSeconds(1));
        }

        Assert.True(match.Success);
        return match.Groups[1].Value;
    }

    private static string FindUp(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var nested = Path.Combine(dir.FullName, "src", Path.GetFileNameWithoutExtension(fileName), fileName);
            if (File.Exists(nested))
            {
                return nested;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(fileName);
    }

    private sealed class FakeClient : IAmqpPeekClient, IAmqpClientFactory
    {
        internal int Gets { get; private set; }

        internal int Rejects { get; private set; }

        internal List<string> Publishes { get; } = [];

        public Task<IAmqpPeekClient> ConnectAsync(AmqpEndpoint endpoint, CancellationToken cancellationToken)
        {
            return Task.FromResult<IAmqpPeekClient>(this);
        }

        public Task<uint> PassiveCountAsync(string queue, CancellationToken cancellationToken)
        {
            return Task.FromResult(1u);
        }

        public Task<PeekedMessage?> GetAsync(string queue, CancellationToken cancellationToken)
        {
            Gets++;
            return Task.FromResult<PeekedMessage?>(new PeekedMessage(9, false, Encoding.UTF8.GetBytes("PAYLOADSECRET"), "tenant-a"));
        }

        public Task RejectRequeueAsync(ulong deliveryTag, CancellationToken cancellationToken)
        {
            Rejects++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AppFactory : WebApplicationFactory<Program>
    {
        internal string DataDir { get; } = Path.Combine(Path.GetTempPath(), "gocgun-bus-" + Guid.NewGuid().ToString("N"));

        internal FakeClient? Client { get; init; }

        internal CollectingLoggerProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(DataDir);
            builder.UseSetting("Diagnostics:DataDirectory", DataDir);
            builder.UseSetting("urls", "http://127.0.0.1:0");
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(Logs);
            });
            if (Client is not null)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IAmqpClientFactory>();
                    services.AddSingleton<IAmqpClientFactory>(Client);
                });
            }
        }
    }

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        internal List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName)
        {
            return new CollectingLogger(Lines);
        }

        public void Dispose()
        {
        }
    }

    private sealed class CollectingLogger : ILogger
    {
        private readonly List<string> lines;

        internal CollectingLogger(List<string> lines)
        {
            this.lines = lines;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lines.Add(formatter(state, exception));
        }
    }
}
