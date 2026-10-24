using System.Text.Json;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Launch;

namespace Archoera.Infrastructure.Settings;

/// <summary>
/// JSON shape and validation for the launch environment and wrapper fields.
/// Global settings and instance overrides both use this helper, so their
/// limits, hostile-name rejection and serialization cannot drift apart.
/// </summary>
internal static class RuntimeOptionsJson
{
    /// <summary>
    /// Validates a wrapper command and its tokenization. Empty input becomes
    /// <c>null</c> (no wrapper).
    /// </summary>
    public static string? NormalizeWrapper(string? value)
    {
        var wrapper = LaunchEnvironmentRules.NormalizeWrapper(value);
        if (wrapper is null) return null;
        try
        {
            var tokens = CommandLineTokenizer.Split(wrapper);
            if (tokens.Count == 0 || tokens[0].Length == 0)
            {
                throw new ArgumentException(
                    "wrapperCommand must start with a non-empty executable");
            }
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"wrapperCommand: {exception.Message}");
        }

        return wrapper;
    }

    /// <summary>
    /// Reads a strict <c>environment</c> patch: JSON null clears the map,
    /// otherwise the value must be an object of string values. Invalid names,
    /// values and hostile JVM variables fail with <see cref="ArgumentException"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? ReadEnvironment(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("environment must be an object or null");
        }

        var result = LaunchEnvironmentRules.CreateMap();
        var seen = 0;
        foreach (var property in element.EnumerateObject())
        {
            seen++;
            if (seen > LaunchEnvironmentRules.MaxEntries)
            {
                throw new ArgumentException(
                    $"environment must have at most {LaunchEnvironmentRules.MaxEntries} entries");
            }

            LaunchEnvironmentRules.ValidateKey(property.Name);
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException(
                    $"environment value for '{property.Name}' must be a string");
            }

            var value = property.Value.GetString() ?? string.Empty;
            LaunchEnvironmentRules.ValidateValue(property.Name, value);
            result[property.Name] = value;
        }

        return result;
    }

    /// <summary>
    /// Lenient variant for <c>.archoera/instance.json</c>, which may be edited
    /// by hand or written by another launcher. Missing or mistyped values yield
    /// <c>null</c>; individual invalid entries are skipped.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? TryReadEnvironment(
        JsonElement parent,
        string name)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var result = LaunchEnvironmentRules.CreateMap();
        foreach (var property in element.EnumerateObject())
        {
            if (result.Count >= LaunchEnvironmentRules.MaxEntries) break;
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            if (!LaunchEnvironmentRules.IsValidName(property.Name)) continue;

            var value = property.Value.GetString() ?? string.Empty;
            if (value.Length > LaunchEnvironmentRules.MaxValueLength) continue;
            result[property.Name] = value;
        }

        return result;
    }

    /// <summary>Writes an <c>environment</c> object with stable key order.</summary>
    public static void WriteEnvironment(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyDictionary<string, string>? environment)
    {
        writer.WriteStartObject(name);
        if (environment is not null)
        {
            foreach (var pair in environment.OrderBy(
                         pair => pair.Key,
                         StringComparer.Ordinal))
            {
                writer.WriteString(pair.Key, pair.Value);
            }
        }

        writer.WriteEndObject();
    }
}
