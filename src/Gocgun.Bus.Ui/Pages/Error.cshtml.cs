using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gocgun.Bus.Ui.Pages;

/// <summary>Generic error page. It shows no exception text.</summary>
[AllowAnonymous]
public sealed class ErrorModel : PageModel
{
    /// <summary>Shows the generic error.</summary>
    public void OnGet()
    {
        // The page renders from its properties. There is no load step.
    }
}
