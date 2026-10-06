using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace Pufferfin.Server.Arr;

/// <summary>
/// Reads and writes the <c>fields</c> of a provider item (download client, notification, application, indexer proxy, custom format condition).
/// </summary>
public static class ArrFields
{
    /// <summary>
    /// Sets the value of a field, when the item has it.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="name">The field name.</param>
    /// <param name="value">The value.</param>
    /// <returns><c>true</c> when the field exists.</returns>
    public static bool Set(JsonObject item, string name, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Find(item, name) is not { } field)
        {
            return false;
        }

        field["value"] = value;
        return true;
    }

    /// <summary>
    /// Gets the value of a field.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    public static JsonNode? Get(JsonObject item, string name)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Find(item, name)?["value"];
    }

    /// <summary>
    /// Finds a template by implementation in a schema answer.
    /// </summary>
    /// <param name="schema">The schema: an array of templates.</param>
    /// <param name="implementation">The implementation name.</param>
    /// <returns>A copy of the template, or <c>null</c>.</returns>
    public static JsonObject? Template(JsonNode? schema, string implementation)
        => (schema as JsonArray)?
            .OfType<JsonObject>()
            .FirstOrDefault(t => string.Equals(t["implementation"]?.GetValue<string>(), implementation, StringComparison.OrdinalIgnoreCase))?
            .DeepClone()
            .AsObject();

    private static JsonObject? Find(JsonObject item, string name)
        => (item["fields"] as JsonArray)?
            .OfType<JsonObject>()
            .FirstOrDefault(f => string.Equals(f["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase));
}
