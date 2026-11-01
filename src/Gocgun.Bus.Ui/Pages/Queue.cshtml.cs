using Gocgun.Bus.Ui.Amqp;
using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gocgun.Bus.Ui.Pages;

/// <summary>Passive queue check. It does not declare a new queue.</summary>
public sealed class QueueModel : PageModel
{
    private readonly SessionStore<BusSession> sessions;
    private readonly IAmqpClientFactory clients;
    private readonly AuditLog audit;

    internal QueueModel(SessionStore<BusSession> sessions, IAmqpClientFactory clients, AuditLog audit)
    {
        this.sessions = sessions;
        this.clients = clients;
        this.audit = audit;
    }

    /// <summary>Gets or sets the queue name.</summary>
    [BindProperty]
    public string Queue { get; set; } = string.Empty;

    /// <summary>Gets the error text.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets the message count.</summary>
    public uint? Count { get; private set; }

    /// <summary>Shows the form.</summary>
    public void OnGet()
    {
        // The page renders from its properties. There is no load step.
    }

    /// <summary>Checks the queue in passive mode.</summary>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>The page.</returns>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Queue))
        {
            Error = "Queue name is required.";
            return Page();
        }

        var session = await BusPages.SessionAsync(HttpContext, sessions);
        if (session.Endpoint is null)
        {
            Error = "Connect before checking a queue.";
            return Page();
        }

        try
        {
            await using var client = await clients.ConnectAsync(session.Endpoint, cancellationToken);
            Count = await client.PassiveCountAsync(Queue.Trim(), cancellationToken);
            BusPages.Audit(audit, session, Queue.Trim(), null, "queue-check");
        }
        catch (InvalidOperationException ex)
        {
            Error = ex.Message;
        }
        catch (Exception)
        {
            Error = "Queue check failed.";
        }

        return Page();
    }
}
