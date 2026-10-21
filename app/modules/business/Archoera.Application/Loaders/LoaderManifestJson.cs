using System.Buffers;
using System.Text.Json;
using Archoera.Domain.Loaders;

namespace Archoera.Application.Loaders;

/// <summary>
/// Wire format for DevHost diagnostics. Written with <see cref="Utf8JsonWriter"/>
/// so NativeAOT never needs reflection-based serialization.
/// </summary>
public static class LoaderManifestJson
{
    public const int WireSchemaVersion = 1;

    public static byte[] BuildEnvelope(
        LoaderArtifactManifest manifest,
        LoaderVerification verification,
        LoaderRepairResult? repair = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(verification);

        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", verification.Ok && (repair?.Ok ?? true));
            writer.WriteStartObject("data");
            writer.WritePropertyName("manifest");
            WriteManifest(writer, manifest);
            writer.WritePropertyName("verification");
            WriteVerification(writer, verification);
            if (repair is not null)
            {
                writer.WritePropertyName("repair");
                WriteRepair(writer, repair);
            }

            writer.WriteEndObject();
            writer.WriteStartObject("meta");
            writer.WriteNumber("schemaVersion", WireSchemaVersion);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static byte[] BuildManifest(LoaderArtifactManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var buffer = new ArrayBufferWriter<byte>(2048);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            WriteManifest(writer, manifest);
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static byte[] BuildVerification(LoaderVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            WriteVerification(writer, verification);
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static byte[] BuildError(string code, string message)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", false);
            writer.WriteStartObject("error");
            writer.WriteString("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteStartObject("meta");
            writer.WriteNumber("schemaVersion", WireSchemaVersion);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteManifest(Utf8JsonWriter writer, LoaderArtifactManifest manifest)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", manifest.SchemaVersion);
        writer.WriteStartObject("loader");
        writer.WriteString("kind", manifest.LoaderId.Kind.ToString().ToLowerInvariant());
        writer.WriteString("gameVersion", manifest.LoaderId.GameVersion);
        writer.WriteString("loaderVersion", manifest.LoaderId.LoaderVersion);
        writer.WriteString("installerSha1", manifest.LoaderId.InstallerSha1);
        writer.WriteEndObject();
        writer.WriteString("installerPath", manifest.InstallerPath);
        writer.WriteBoolean("installerOutsideRoot", manifest.InstallerOutsideRoot);
        writer.WriteString("versionJsonPath", manifest.VersionJsonPath);
        writer.WriteString("versionId", manifest.VersionId);
        writer.WriteString("librariesPath", manifest.LibrariesPath);
        writer.WriteNumber("processorCount", manifest.ProcessorCount);
        writer.WriteString("createdAt", manifest.CreatedAt);

        writer.WriteStartArray("artifacts");
        foreach (var artifact in manifest.Artifacts)
        {
            WriteArtifact(writer, artifact);
        }

        writer.WriteEndArray();

        writer.WriteStartArray("processors");
        foreach (var processor in manifest.Processors)
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", processor.Index);
            writer.WriteString("jarRelativePath", processor.JarRelativePath);
            writer.WriteStartArray("classpath");
            foreach (var entry in processor.ClassPath)
            {
                writer.WriteStringValue(entry);
            }

            writer.WriteEndArray();
            writer.WriteString("mainClass", processor.MainClass);
            writer.WriteStartArray("arguments");
            foreach (var argument in processor.Arguments)
            {
                writer.WriteStringValue(argument);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("outputs");
            foreach (var output in processor.Outputs)
            {
                writer.WriteStringValue(output.RelativePath);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteArtifact(Utf8JsonWriter writer, LoaderArtifact artifact)
    {
        writer.WriteStartObject();
        writer.WriteString("key", artifact.Key);
        writer.WriteString("relativePath", artifact.RelativePath);
        if (artifact.Sha1 is null)
        {
            writer.WriteNull("sha1");
        }
        else
        {
            writer.WriteString("sha1", artifact.Sha1);
        }

        if (artifact.Size is null)
        {
            writer.WriteNull("size");
        }
        else
        {
            writer.WriteNumber("size", artifact.Size.Value);
        }

        writer.WriteNumber("producerIndex", artifact.ProducerIndex);
        writer.WriteBoolean("requiredForLaunch", artifact.RequiredForLaunch);
        writer.WriteString("kind", artifact.Kind == LoaderArtifactKind.Generated ? "generated" : "extracted");
        writer.WriteString("location", artifact.Location == LoaderArtifactLocation.Root ? "root" : "libraries");
        writer.WriteEndObject();
    }

    private static void WriteVerification(Utf8JsonWriter writer, LoaderVerification verification)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("ok", verification.Ok);
        writer.WriteStartArray("missing");
        foreach (var artifact in verification.Missing)
        {
            WriteArtifactRef(writer, artifact);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("mismatched");
        foreach (var artifact in verification.Mismatched)
        {
            WriteArtifactRef(writer, artifact);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("unsafe");
        foreach (var artifact in verification.Unsafe ?? [])
        {
            WriteArtifactRef(writer, artifact);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("details");
        foreach (var detail in verification.Details)
        {
            writer.WriteStringValue(detail);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteArtifactRef(Utf8JsonWriter writer, LoaderArtifact artifact)
    {
        writer.WriteStartObject();
        writer.WriteString("key", artifact.Key);
        writer.WriteString("relativePath", artifact.RelativePath);
        writer.WriteString("location", artifact.Location == LoaderArtifactLocation.Root ? "root" : "libraries");
        writer.WriteBoolean("requiredForLaunch", artifact.RequiredForLaunch);
        writer.WriteEndObject();
    }

    private static void WriteRepair(Utf8JsonWriter writer, LoaderRepairResult repair)
    {
        writer.WriteStartObject();
        writer.WriteString("status", repair.Status switch
        {
            LoaderRepairStatus.AlreadyValid => "alreadyValid",
            LoaderRepairStatus.Repaired => "repaired",
            _ => "failed",
        });
        writer.WriteBoolean("ok", repair.Ok);
        writer.WriteStartArray("processorsRun");
        foreach (var index in repair.ProcessorsRun)
        {
            writer.WriteNumberValue(index);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("details");
        foreach (var detail in repair.Details)
        {
            writer.WriteStringValue(detail);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
