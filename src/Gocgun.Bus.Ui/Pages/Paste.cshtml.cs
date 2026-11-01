using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Decoding;
using Gocgun.Diagnostics.Shared.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gocgun.Bus.Ui.Pages;

/// <summary>Decodes a pasted body. The audit line does not include the body.</summary>
public sealed class PasteModel : PageModel
{
    private readonly SessionStore<BusSession> sessions;
    private readonly AuditLog audit;
    private readonly DiagnosticsOptions options;

    internal PasteModel(SessionStore<BusSession> sessions, AuditLog audit, DiagnosticsOptions options)
    {
        this.options = options;
        this.sessions = sessions;
        this.audit = audit;
    }

    /// <summary>Gets or sets the base64 body.</summary>
    [BindProperty]
    public string Body { get; set; } = string.Empty;

    /// <summary>Gets or sets an optional contract override.</summary>
    [BindProperty]
    public string TypeOverride { get; set; } = string.Empty;

    /// <summary>Gets or sets raw AMQP header lines.</summary>
    [BindProperty]
    public string HeaderText { get; set; } = string.Empty;

    /// <summary>Gets or sets the contract folder.</summary>
    [BindProperty]
    public string Folder { get; set; } = string.Empty;

    /// <summary>Gets or sets the tenant filter.</summary>
    [BindProperty]
    public string Tenant { get; set; } = string.Empty;

    /// <summary>Gets the error text.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets the decoded text.</summary>
    public string Display { get; private set; } = string.Empty;

    /// <summary>Shows the form.</summary>
    public void OnGet()
    {
        // The page renders from its properties. There is no load step.
    }

    /// <summary>Decodes the paste.</summary>
    /// <returns>The page.</returns>
    public async Task<IActionResult> OnPostAsync()
    {
        var session = await BusPages.SessionAsync(HttpContext, sessions);
        if (!string.IsNullOrWhiteSpace(Folder))
        {
            try
            {
                session.Catalog = ContractLoader.Load(Folder, options, audit, session.Profile);
            }
            catch (DirectoryNotFoundException)
            {
                Error = "Contract folder does not exist.";
                return Page();
            }
            catch (InvalidOperationException ex)
            {
                Error = ex.Message;
                return Page();
            }
        }

        var headers = ParseHeaders(HeaderText);
        var decoded = BusBodyDecoder.DecodePaste(Body, session.Catalog, TypeOverride, headers, Tenant);
        if (decoded.IsUserError)
        {
            Error = decoded.Error;
            return Page();
        }

        Display = decoded.Display;
        BusPages.Audit(audit, session, "paste", decoded.PayloadType, "paste");
        return Page();
    }

    private static Dictionary<string, string> ParseHeaders(string? text)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return headers;
        }

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var split = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (split.Length == 2)
            {
                headers[split[0]] = split[1];
            }
        }

        return headers;
    }
}
