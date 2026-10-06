namespace Pufferfin.Server.Requests;

/// <summary>
/// What a user may request. Administrators may always request, without approval nor quota.
/// </summary>
public sealed record RequestPermissions
{
    /// <summary>
    /// Gets the permissions of a user an administrator never set.
    /// </summary>
    public static RequestPermissions Default { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the user may request titles.
    /// </summary>
    public bool CanRequest { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether an administrator must approve each request of the user.
    /// </summary>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// Gets the number of requests the user may make in seven days, or <c>null</c> for no limit.
    /// </summary>
    public int? WeeklyQuota { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user may delete titles from the library, with the safe mode only.
    /// Off until an administrator turns it on.
    /// </summary>
    public bool CanDelete { get; init; }
}
