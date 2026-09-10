using ControlPlane.Api.Models;
using Microsoft.AspNetCore.Http;

namespace ControlPlane.Api.Security;

public static class PanelSessionExtensions
{
    public const string CredentialStampKey = "panel-credential-stamp";

    public static void SignInPanelUser(this ISession session, string userId, string email, string displayName, string role, string? credentialStamp = null)
    {
        session.SetString(PanelSecurityConstants.SessionUserId, userId);
        session.SetString(PanelSecurityConstants.SessionUserEmail, email);
        session.SetString(PanelSecurityConstants.SessionUserDisplayName, displayName);
        session.SetString(PanelSecurityConstants.SessionUserRole, PanelSecurityConstants.NormalizeRole(role));
        if (credentialStamp is not null) session.SetString(CredentialStampKey, credentialStamp);
    }

    public static void SignOutPanelUser(this ISession session)
    {
        session.Clear();
        session.Remove(PanelSecurityConstants.SessionUserId);
        session.Remove(PanelSecurityConstants.SessionUserEmail);
        session.Remove(PanelSecurityConstants.SessionUserDisplayName);
        session.Remove(PanelSecurityConstants.SessionUserRole);
    }

    public static PanelCurrentUserViewModel? GetCurrentPanelUser(this HttpContext httpContext)
    {
        var session = httpContext.Session;
        var userId = session.GetString(PanelSecurityConstants.SessionUserId);
        var email = session.GetString(PanelSecurityConstants.SessionUserEmail);
        var displayName = session.GetString(PanelSecurityConstants.SessionUserDisplayName);
        var role = session.GetString(PanelSecurityConstants.SessionUserRole);

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        return new PanelCurrentUserViewModel
        {
            UserId = userId,
            Email = email,
            DisplayName = displayName,
            Role = PanelSecurityConstants.NormalizeRole(role),
            IsAdmin = PanelSecurityConstants.IsAdminRole(role)
        };
    }
}
