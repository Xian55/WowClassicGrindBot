using System;

namespace Core;

[AttributeUsage(AttributeTargets.Method)]
public sealed class NamesAttribute(string[] values) : Attribute
{
    public string[] Values { get; } = values;
}