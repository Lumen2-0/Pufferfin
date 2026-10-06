using System;
using System.Collections.Generic;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Tests.TestHelpers;

public sealed class FakeEnvironment : IEnvironmentReader
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);

    public FakeEnvironment()
    {
    }

    public FakeEnvironment(IReadOnlyDictionary<string, string> variables)
    {
        foreach (var (name, value) in variables)
        {
            _variables[name] = value;
        }
    }

    public string? Get(string name) => _variables.GetValueOrDefault(name);
}
