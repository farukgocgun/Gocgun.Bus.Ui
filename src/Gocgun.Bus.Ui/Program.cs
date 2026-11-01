using Gocgun.Bus.Ui.Amqp;
using Gocgun.Diagnostics.Shared.Hosting;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gocgun.Bus.Ui;

/// <summary>Entry point for the bus diagnostic tool.</summary>
public partial class Program
{
    /// <summary>Starts Kestrel on loopback. <c>--urls</c> must also be a loopback host.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The host task.</returns>
    public static Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        DiagnosticsHost.Add(builder, "gocgun-bus-ui");
        builder.Services.AddSingleton<SessionStore<BusSession>>();
        builder.Services.AddSingleton<ISessionSweeper>(services => services.GetRequiredService<SessionStore<BusSession>>());
        builder.Services.AddSingleton<IAmqpClientFactory, RabbitClientFactory>();
        builder.Services.RemoveAll<IPageModelActivatorProvider>();
        builder.Services.AddSingleton<IPageModelActivatorProvider, PageActivator>();
        var app = builder.Build();
        DiagnosticsHost.Use(app);
        app.MapRazorPages();
        return app.RunAsync();
    }
}
