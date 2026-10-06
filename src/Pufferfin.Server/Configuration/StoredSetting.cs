namespace Pufferfin.Server.Configuration;

/// <summary>
/// A setting as persisted in the store file.
/// </summary>
/// <param name="Value">The normalized value; <c>null</c> when an administrator cleared the setting.</param>
/// <param name="FromEnvironment">
/// <c>true</c> when the value was copied from the environment and never edited since.
/// Such a value follows the environment variable while it is present.
/// </param>
public sealed record StoredSetting(string? Value, bool FromEnvironment)
{
    /// <inheritdoc />
    public override string ToString() => $"{nameof(StoredSetting)} {{ {nameof(FromEnvironment)} = {FromEnvironment} }}";
}
