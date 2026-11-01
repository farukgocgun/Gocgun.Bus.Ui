using Gocgun.Bus.Ui.Amqp;
using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Decoding;
using Gocgun.Diagnostics.Shared.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gocgun.Bus.Ui.Pages;

/// <summary>Peeks the queue head and requeues it. There is no requeue=false path.</summary>
public sealed class PeekModel : PageModel
{
    private readonly SessionStore<BusSession> sessions;
    private readonly IAmqpClientFactory clients;
    private readonly AuditLog audit;

    internal PeekModel(SessionStore<BusSession> sessions, IAmqpClientFactory clients, AuditLog audit)
    {
        this.sessions = sessions;
        this.clients = clients;
        this.audit = audit;
    }

    /// <summary>Gets or sets the queue name.</summary>
    [BindProperty]
    public string Queue { get; set; } = string.Empty;

    /// <summary>Gets or sets the tenant filter.</summary>
    [BindProperty]
    public string Tenant { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the operator confirmed a live peek.</summary>
    [BindProperty]
    public string ConfirmLive { get; set; } = string.Empty;

    /// <summary>Gets the error text.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets the decoded text.</summary>
    public string Display { get; private set; } = string.Empty;

    /// <summary>Shows the warning.</summary>
    public void OnGet()
    {
        // The page renders from its properties. There is no load step.
    }

    /// <summary>Gets one message and rejects it with requeue.</summary>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>The page.</returns>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Queue))
        {
            Error = "Queue name is required.";
            return Page();
        }

        if (!string.Equals(ConfirmLive, "yes", StringComparison.Ordinal))
        {
            Error = "Confirm the requeue side effects before peeking.";
            return Page();
        }

        var session = await BusPages.SessionAsync(HttpContext, sessions);
        if (session.Endpoint is null)
        {
            Error = "Connect before peeking.";
            return Page();
        }

        try
        {
            await using var client = await clients.ConnectAsync(session.Endpoint, cancellationToken);
            var message = await QueuePeek.RunAsync(client, Queue.Trim(), cancellationToken);
            if (message is null)
            {
                Display = "Queue head is empty.";
            }
            else
            {
                var headers = message.Tenant is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal) { [Gocgun.Diagnostics.Shared.Tenancy.TenantMarkers.AmqpHeaderName] = message.Tenant };
                var decoded = BusBodyDecoder.Decode(message.Body, session.Catalog, null, headers, Tenant);
                Display = "redelivered-before-peek " + message.Redelivered.ToString() + "\n" + decoded.Display;
            }

            BusPages.Audit(audit, session, Queue.Trim(), null, "peek");
        }
        catch (InvalidOperationException ex)
        {
            Error = ex.Message;
        }
        catch (Exception)
        {
            Error = "AMQP read failed.";
        }

        return Page();
    }
}
