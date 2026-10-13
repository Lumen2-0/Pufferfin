namespace Pufferfin.Server.Stack;

/// <summary>
/// A management page of the stack, opened by administrators from the profile menu.
/// </summary>
/// <param name="Name">The shown name, such as Radarr.</param>
/// <param name="Url">The absolute http or https address of the page.</param>
public sealed record StackLink(string Name, string Url);
