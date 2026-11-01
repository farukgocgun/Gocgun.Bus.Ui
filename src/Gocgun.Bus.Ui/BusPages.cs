using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Hosting;

namespace Gocgun.Bus.Ui;

internal static class BusPages
{
    internal static ValueTask<BusSession> SessionAsync(HttpContext http, SessionStore<BusSession> store)
    {
        var sid = DiagnosticsHost.Sid(http.User) ?? "missing";
        return store.GetOrAddAsync(sid, _ => new BusSession());
    }

    internal static void Audit(AuditLog log, BusSession session, string target, string? contract, string action)
    {
        log.Append(new AuditEntry(DiagnosticsHost.OperatorName, session.Profile, target, contract, action));
    }
}
