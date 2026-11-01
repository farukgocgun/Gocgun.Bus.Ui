using System.Globalization;
using Gocgun.Bus.Ui.Amqp;
using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Hosting;
using Gocgun.Diagnostics.Shared.Profiles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gocgun.Bus.Ui.Pages;

/// <summary>AMQP connection form. The password is not written to the audit log.</summary>
public sealed class IndexModel : PageModel
{
    private readonly SessionStore<BusSession> sessions;
    private readonly ProtectedProfileStore profiles;
    private readonly AuditLog audit;
    private readonly DiagnosticsOptions options;
    private readonly ILogger<IndexModel> logger;

    internal IndexModel(SessionStore<BusSession> sessions, ProtectedProfileStore profiles, AuditLog audit, DiagnosticsOptions options, ILogger<IndexModel> logger)
    {
        this.options = options;
        this.sessions = sessions;
        this.profiles = profiles;
        this.audit = audit;
        this.logger = logger;
    }

    /// <summary>Gets or sets the host.</summary>
    [BindProperty]
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Gets or sets the port.</summary>
    [BindProperty]
    public int Port { get; set; } = 5672;

    /// <summary>Gets or sets the virtual host.</summary>
    [BindProperty]
    public string VirtualHost { get; set; } = "/";

    /// <summary>Gets or sets the user name.</summary>
    [BindProperty]
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the password.</summary>
    [BindProperty]
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether TLS is used.</summary>
    [BindProperty]
    public bool Tls { get; set; }

    /// <summary>Gets or sets the profile name.</summary>
    [BindProperty]
    public string ProfileName { get; set; } = "session";

    /// <summary>Gets or sets a value indicating whether the profile is saved.</summary>
    [BindProperty]
    public bool SaveProfile { get; set; }

    /// <summary>Gets the form error.</summary>
    public string? Error { get; private set; }

    /// <summary>Shows the form.</summary>
    public void OnGet()
    {
        // The page renders from its properties. There is no load step.
    }

    /// <summary>Keeps the endpoint in memory for this login.</summary>
    /// <returns>The paste page or the form.</returns>
    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            Error = "Host is required.";
            return Page();
        }

        if (Port is < 1 or > 65535)
        {
            Error = "Port must be between 1 and 65535.";
            return Page();
        }

        if (!options.IsHostAllowed(Host, Port))
        {
            audit.Append(new AuditEntry(DiagnosticsHost.OperatorName, "-", Host.Trim() + ":" + Port.ToString(CultureInfo.InvariantCulture), null, "connect-refused"));
            Error = "Host is not in Diagnostics:AllowedHosts.";
            return Page();
        }

        if (!ProtectedProfileStore.IsValidName(ProfileName?.Trim()))
        {
            Error = ProtectedProfileStore.NameRule;
            return Page();
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("AMQP connect host {Host} port {Port}", Host, Port);
        }

        var session = await BusPages.SessionAsync(HttpContext, sessions);
        session.Endpoint = new AmqpEndpoint(Host.Trim(), Port, VirtualHost ?? "/", UserName ?? string.Empty, Password ?? string.Empty, Tls);
        session.Profile = string.IsNullOrWhiteSpace(ProfileName) ? "session" : ProfileName.Trim();
        if (SaveProfile)
        {
            profiles.Save(session.Profile, Password ?? string.Empty, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["host"] = Host.Trim(),
                ["port"] = Port.ToString(CultureInfo.InvariantCulture),
                ["vhost"] = VirtualHost ?? "/",
                ["user"] = UserName ?? string.Empty,
                ["tls"] = Tls ? "true" : "false",
            });
        }

        BusPages.Audit(audit, session, session.Endpoint.ToString(), null, "connect");
        return RedirectToPage("/Paste");
    }
}
