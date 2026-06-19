namespace ControlPlane.Api.Security;

public static class PanelSecurityConstants
{
    public const string SessionUserId = "panel-user-id";
    public const string SessionUserEmail = "panel-user-email";
    public const string SessionUserDisplayName = "panel-user-display-name";
    public const string SessionUserRole = "panel-user-role";

    public const string RoleAdmin = "admin";
    public const string RoleOperator = "operator";

    public static bool IsAdminRole(string? role)
        => string.Equals(role?.Trim(), RoleAdmin, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeRole(string? role, string fallback = RoleOperator)
    {
        if (IsAdminRole(role))
        {
            return RoleAdmin;
        }

        return string.Equals(role?.Trim(), RoleOperator, StringComparison.OrdinalIgnoreCase)
            ? RoleOperator
            : fallback;
    }
}
