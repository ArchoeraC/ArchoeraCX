using System.Text.Json;
using Archoera.Application.Json;

namespace Archoera.Infrastructure.Modules;

/// <summary>
/// Shared JSON envelope helpers for module handlers. The serialization lives in
/// <see cref="JsonEnvelope"/> so the Application layer can use the same
/// primitive; this facade is the single entry point for Infrastructure.
/// </summary>
public static class ModuleJson
{
    public static byte[] Envelope(Action<Utf8JsonWriter> data, bool ok = true)
        => JsonEnvelope.Envelope(data, ok);

    public static byte[] Error(string code, string message)
        => JsonEnvelope.Error(code, message);
}
