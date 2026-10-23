using System.Collections;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace Somning.App.Logging;

/// <summary>
/// Destructuring policy that renders any property whose name contains key, token or authorization
/// (case-insensitive) as "***" (D-23, T-01-19). Objects without sensitive properties fall through to the default.
/// </summary>
public sealed class RedactionPolicy : IDestructuringPolicy
{
    public const string Mask = "***";
    private static readonly string[] SensitiveFragments = ["key", "token", "authorization"];

    public static bool IsSensitive(string name) =>
        SensitiveFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        result = null!;
        if (value is string or IEnumerable || value.GetType().IsPrimitive || value.GetType().IsEnum) return false;

        var properties = value.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToArray();
        if (!properties.Any(property => IsSensitive(property.Name))) return false;

        var rendered = properties.Select(property => new LogEventProperty(property.Name, IsSensitive(property.Name)
            ? new ScalarValue(Mask)
            : propertyValueFactory.CreatePropertyValue(ReadSafely(property, value), destructureObjects: true)));
        var typeTag = value.GetType().Name.StartsWith("<>", StringComparison.Ordinal) ? null : value.GetType().Name;
        result = new StructureValue(rendered, typeTag);
        return true;
    }

    private static object? ReadSafely(PropertyInfo property, object owner)
    {
        try { return property.GetValue(owner); }
        catch (TargetInvocationException ex) { return $"<threw {ex.InnerException?.GetType().Name}>"; }
    }
}
