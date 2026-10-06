using Pufferfin.Server.Requests;

namespace Pufferfin.Server.Api.Models;

/// <summary>
/// What a user may request.
/// </summary>
public sealed class RequestPermissionsDto
{
    /// <summary>Gets or sets a value indicating whether the user may request titles.</summary>
    public bool CanRequest { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether an administrator must approve each request of the user.</summary>
    public bool RequiresApproval { get; set; }

    /// <summary>Gets or sets the number of requests the user may make in seven days, or <c>null</c> for no limit.</summary>
    public int? WeeklyQuota { get; set; }

    /// <summary>Gets or sets the number of requests the user made in the last seven days. Read only.</summary>
    public int UsedThisWeek { get; set; }

    /// <summary>Gets or sets a value indicating whether the user is an administrator, who may always request. Read only.</summary>
    public bool IsAdministrator { get; set; }

    /// <summary>
    /// Maps permissions.
    /// </summary>
    /// <param name="permissions">The permissions.</param>
    /// <param name="usedThisWeek">The requests made in the last seven days.</param>
    /// <param name="isAdministrator">Whether the user is an administrator.</param>
    /// <returns>The DTO.</returns>
    public static RequestPermissionsDto From(RequestPermissions permissions, int usedThisWeek, bool isAdministrator)
        => new()
        {
            CanRequest = isAdministrator || permissions.CanRequest,
            RequiresApproval = !isAdministrator && permissions.RequiresApproval,
            WeeklyQuota = isAdministrator ? null : permissions.WeeklyQuota,
            UsedThisWeek = usedThisWeek,
            IsAdministrator = isAdministrator
        };
}
