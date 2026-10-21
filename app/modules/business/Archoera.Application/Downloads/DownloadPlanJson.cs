using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Archoera.Domain.Downloads;

namespace Archoera.Application.Downloads;

/// <summary>
/// AOT-safe wire format for <see cref="DownloadPlan"/>. Both directions use
/// <see cref="Utf8JsonWriter"/>/<see cref="JsonDocument"/> only, so NativeAOT
/// never needs reflection-based serialization. The emitted shape is exactly
/// what <c>app/modules/downloader/src/plan.rs</c> parses; unknown properties
/// are ignored on read, and the legacy top-level <c>sha1/sha256/sha512</c>
/// fields are accepted for compatibility but never written.
/// </summary>
public static class DownloadPlanJson
{
    /// <summary>
    /// Serializes a validated plan as UTF-8 JSON. Throws
    /// <see cref="DownloadPlanException"/> for a plan the executor would reject,
    /// so a broken plan is caught before it reaches C++/Rust.
    /// </summary>
    public static byte[] Build(DownloadPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        DownloadPlanValidator.EnsureValid(plan);

        var capacity = 1_024 + (plan.Files.Count * 256);
        var buffer = new ArrayBufferWriter<byte>(capacity);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            WritePlan(writer, plan);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Parses and validates a UTF-8 plan. On failure <paramref name="plan"/> is
    /// <see langword="null"/> and <paramref name="error"/> carries a stable code.
    /// </summary>
    public static bool TryParse(
        ReadOnlyMemory<byte> utf8,
        [NotNullWhen(true)] out DownloadPlan? plan,
        out DownloadPlanIssue? error)
    {
        plan = null;
        try
        {
            using var document = JsonDocument.Parse(utf8);
            return TryParse(document.RootElement, out plan, out error);
        }
        catch (JsonException exception)
        {
            error = new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"plan is not valid JSON: {exception.Message}");
            return false;
        }
        catch (ArgumentException exception)
        {
            error = new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"plan is not valid UTF-8 JSON: {exception.Message}");
            return false;
        }
    }

    /// <summary>String overload of <see cref="TryParse(ReadOnlyMemory{byte}, out DownloadPlan?, out DownloadPlanIssue?)"/>.</summary>
    public static bool TryParse(
        string json,
        [NotNullWhen(true)] out DownloadPlan? plan,
        out DownloadPlanIssue? error)
    {
        plan = null;

        if (json is null)
        {
            error = new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "plan is required");
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return TryParse(document.RootElement, out plan, out error);
        }
        catch (JsonException exception)
        {
            error = new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"plan is not valid JSON: {exception.Message}");
            return false;
        }
    }

    private static bool TryParse(
        JsonElement root,
        [NotNullWhen(true)] out DownloadPlan? plan,
        out DownloadPlanIssue? error)
    {
        plan = null;

        if (root.ValueKind != JsonValueKind.Object)
        {
            error = Invalid("plan must be a JSON object");
            return false;
        }

        if (!TryReadOptionalInt32(root, "schemaVersion", DownloadPlanLimits.SchemaVersion, out var schemaVersion, out error)
            || !TryReadOptionalString(root, "planId", string.Empty, out var planId, out error)
            || !TryReadOptionalString(root, "root", string.Empty, out var rootPath, out error)
            || !TryReadOptionalInt32(root, "concurrency", DownloadPlanLimits.DefaultConcurrency, out var concurrency, out error)
            || !TryReadOptionalInt32(root, "priority", 0, out var priority, out error)
            || !TryReadOptionalInt64(root, "bandwidthLimit", 0, out var bandwidthLimit, out error))
        {
            return false;
        }

        if (!TryReadOptions(root, out var options, out error)
            || !TryReadFiles(root, out var files, out error)
            || !TryReadPost(root, out var post, out error))
        {
            return false;
        }

        var candidate = new DownloadPlan
        {
            SchemaVersion = schemaVersion,
            PlanId = planId,
            Root = rootPath,
            Concurrency = concurrency,
            Priority = priority,
            BandwidthLimit = bandwidthLimit,
            // The executor recomputes the sum; normalize so the in-memory model
            // always agrees with files[].
            TotalBytes = DownloadPlanValidator.ComputeTotalBytes(files),
            Options = options,
            Files = files,
            Post = post,
        };

        error = DownloadPlanValidator.Validate(candidate);
        if (error is not null)
        {
            return false;
        }

        plan = candidate;
        return true;
    }

    private static void WritePlan(Utf8JsonWriter writer, DownloadPlan plan)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", plan.SchemaVersion);
        writer.WriteString("planId", plan.PlanId);
        writer.WriteString("root", plan.Root);
        writer.WriteNumber("concurrency", plan.Concurrency);
        writer.WriteNumber("priority", plan.Priority);
        writer.WriteNumber("bandwidthLimit", plan.BandwidthLimit);
        writer.WriteNumber("totalBytes", plan.TotalBytes);

        writer.WriteStartObject("options");
        writer.WriteBoolean("dedup", plan.Options.Dedup);
        writer.WriteString("verifyExisting", plan.Options.VerifyExisting.ToWire());
        writer.WriteString("durability", plan.Options.Durability.ToWire());
        writer.WriteNumber("rangeThreshold", plan.Options.RangeThreshold);
        writer.WriteNumber("chunkSize", plan.Options.ChunkSize);
        writer.WriteNumber("progressIntervalMs", plan.Options.ProgressIntervalMs);
        writer.WriteEndObject();

        writer.WriteStartArray("files");
        foreach (var file in plan.Files)
        {
            WriteFile(writer, file);
        }

        writer.WriteEndArray();

        writer.WriteStartArray("post");
        foreach (var action in plan.Post)
        {
            WritePostAction(writer, action);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteFile(Utf8JsonWriter writer, PlanFile file)
    {
        writer.WriteStartObject();
        writer.WriteString("url", file.Url);

        writer.WriteStartArray("mirrors");
        foreach (var mirror in file.Mirrors)
        {
            writer.WriteStringValue(mirror);
        }

        writer.WriteEndArray();

        writer.WriteStartObject("hash");
        writer.WriteString("algo", file.Hash.Algo);
        writer.WriteString("hex", file.Hash.Hex);
        writer.WriteEndObject();

        writer.WriteNumber("size", file.Size);
        writer.WriteString("dest", file.Dest);
        writer.WriteString("mode", file.Mode.ToWire());
        writer.WriteNumber("priority", file.Priority);
        writer.WriteBoolean("executable", file.Executable);

        writer.WriteStartObject("headers");
        foreach (var (name, value) in file.Headers)
        {
            writer.WriteString(name, value);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WritePostAction(Utf8JsonWriter writer, DownloadPostAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Json))
        {
            throw new DownloadPlanException(
                DownloadPlanErrorCode.InvalidPlan,
                "post[] entry is empty");
        }

        try
        {
            writer.WriteRawValue(action.Json, skipInputValidation: false);
        }
        catch (JsonException exception)
        {
            throw new DownloadPlanException(
                DownloadPlanErrorCode.InvalidPlan,
                $"post[] entry is not valid JSON: {exception.Message}");
        }
    }

    private static bool TryReadOptions(
        JsonElement root,
        out DownloadPlanOptions options,
        out DownloadPlanIssue? error)
    {
        options = DownloadPlanOptions.Default;
        error = null;

        if (!root.TryGetProperty("options", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = Invalid("options must be an object");
            return false;
        }

        if (!TryReadOptionalBool(element, "dedup", true, out var dedup, out error)
            || !TryReadOptionalInt64(element, "rangeThreshold", DownloadPlanLimits.DefaultRangeThreshold, out var rangeThreshold, out error)
            || !TryReadOptionalInt64(element, "chunkSize", DownloadPlanLimits.DefaultChunkSize, out var chunkSize, out error)
            || !TryReadOptionalInt32(element, "progressIntervalMs", DownloadPlanLimits.DefaultProgressIntervalMs, out var progressIntervalMs, out error))
        {
            return false;
        }

        var verifyExisting = DownloadVerifyExisting.Size;
        if (element.TryGetProperty("verifyExisting", out var verifyElement))
        {
            if (verifyElement.ValueKind != JsonValueKind.String
                || !DownloadWire.TryParseVerifyExisting(verifyElement.GetString(), out verifyExisting))
            {
                error = Invalid("options.verifyExisting must be 'size', 'hash' or 'none'");
                return false;
            }
        }

        var durability = DownloadDurability.Normal;
        if (element.TryGetProperty("durability", out var durabilityElement))
        {
            if (durabilityElement.ValueKind != JsonValueKind.String
                || !DownloadWire.TryParseDurability(durabilityElement.GetString(), out durability))
            {
                error = Invalid("options.durability must be 'normal', 'full' or 'fast'");
                return false;
            }
        }

        options = new DownloadPlanOptions
        {
            Dedup = dedup,
            VerifyExisting = verifyExisting,
            Durability = durability,
            RangeThreshold = rangeThreshold,
            ChunkSize = chunkSize,
            ProgressIntervalMs = progressIntervalMs,
        };

        return true;
    }

    private static bool TryReadFiles(
        JsonElement root,
        out IReadOnlyList<PlanFile> files,
        out DownloadPlanIssue? error)
    {
        files = [];
        error = null;

        if (!root.TryGetProperty("files", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            error = Invalid("files must be an array");
            return false;
        }

        var parsed = new List<PlanFile>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                error = Invalid("files[] entries must be objects");
                return false;
            }

            if (!TryReadFile(item, out var file, out error))
            {
                return false;
            }

            parsed.Add(file);
        }

        files = parsed;
        return true;
    }

    private static bool TryReadFile(
        JsonElement item,
        out PlanFile file,
        out DownloadPlanIssue? error)
    {
        file = null!;

        if (!TryReadRequiredString(item, "url", out var url, out error)
            || !TryReadRequiredString(item, "dest", out var dest, out error)
            || !TryReadMirrors(item, out var mirrors, out error)
            || !TryReadHash(item, dest, out var hash, out error)
            || !TryReadOptionalInt64(item, "size", 0, out var size, out error)
            || !TryReadOptionalInt32(item, "priority", 0, out var priority, out error)
            || !TryReadOptionalBool(item, "executable", false, out var executable, out error)
            || !TryReadHeaders(item, out var headers, out error))
        {
            return false;
        }

        var mode = DownloadFileMode.Ensure;
        if (item.TryGetProperty("mode", out var modeElement))
        {
            if (modeElement.ValueKind != JsonValueKind.String
                || !DownloadWire.TryParseFileMode(modeElement.GetString(), out mode))
            {
                error = Invalid($"{dest}: mode must be one of " +
                    "ensure/replace/link/symlink/skip");
                return false;
            }
        }

        file = new PlanFile(url, mirrors, hash, size, dest, mode, priority, executable)
        {
            Headers = headers,
        };

        return true;
    }

    private static bool TryReadMirrors(
        JsonElement item,
        out IReadOnlyList<string> mirrors,
        out DownloadPlanIssue? error)
    {
        mirrors = [];
        error = null;

        if (!item.TryGetProperty("mirrors", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            error = Invalid("files[].mirrors must be an array");
            return false;
        }

        var parsed = new List<string>(element.GetArrayLength());
        foreach (var mirror in element.EnumerateArray())
        {
            if (mirror.ValueKind != JsonValueKind.String)
            {
                error = Invalid("files[].mirrors entries must be strings");
                return false;
            }

            parsed.Add(mirror.GetString() ?? string.Empty);
        }

        mirrors = parsed;
        return true;
    }

    private static bool TryReadHash(
        JsonElement item,
        string dest,
        out DownloadHash hash,
        out DownloadPlanIssue? error)
    {
        hash = null!;
        error = null;

        if (item.TryGetProperty("hash", out var element) && element.ValueKind != JsonValueKind.Null)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                error = new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidHash,
                    $"{dest}: hash must be an object");
                return false;
            }

            if (!TryReadRequiredString(element, "algo", out var algo, out error)
                || !TryReadRequiredString(element, "hex", out var hex, out error))
            {
                error = new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidHash,
                    $"{dest}: {error?.Message}");
                return false;
            }

            if (!DownloadHash.TryCreate(algo, hex, out hash))
            {
                error = new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidHash,
                    $"{dest}: unsupported hash algo '{algo}'");
                return false;
            }

            return true;
        }

        // Legacy compatibility: Rust normalizes top-level sha1/sha256/sha512.
        foreach (var legacy in new[] { "sha512", "sha256", "sha1" })
        {
            if (item.TryGetProperty(legacy, out var legacyElement)
                && legacyElement.ValueKind == JsonValueKind.String)
            {
                if (!DownloadHash.TryCreate(legacy, legacyElement.GetString(), out hash))
                {
                    error = new DownloadPlanIssue(
                        DownloadPlanErrorCode.InvalidHash,
                        $"{dest}: unsupported hash algo '{legacy}'");
                    return false;
                }

                return true;
            }
        }

        error = new DownloadPlanIssue(
            DownloadPlanErrorCode.InvalidHash,
            $"{dest}: hash is required");
        return false;
    }

    private static bool TryReadHeaders(
        JsonElement item,
        out IReadOnlyDictionary<string, string> headers,
        out DownloadPlanIssue? error)
    {
        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = null;

        if (!item.TryGetProperty("headers", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = Invalid("files[].headers must be an object");
            return false;
        }

        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in element.EnumerateObject())
        {
            if (header.Value.ValueKind != JsonValueKind.String)
            {
                error = Invalid($"files[].headers['{header.Name}'] must be a string");
                return false;
            }

            parsed[header.Name] = header.Value.GetString() ?? string.Empty;
        }

        headers = parsed;
        return true;
    }

    private static bool TryReadPost(
        JsonElement root,
        out IReadOnlyList<DownloadPostAction> post,
        out DownloadPlanIssue? error)
    {
        post = [];
        error = null;

        if (!root.TryGetProperty("post", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            error = Invalid("post must be an array");
            return false;
        }

        var parsed = new List<DownloadPostAction>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            parsed.Add(new DownloadPostAction(item.GetRawText()));
        }

        post = parsed;
        return true;
    }

    private static bool TryReadRequiredString(
        JsonElement parent,
        string name,
        out string value,
        out DownloadPlanIssue? error)
    {
        value = string.Empty;
        error = null;

        if (!parent.TryGetProperty(name, out var element))
        {
            error = Invalid($"{name} is required");
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = Invalid($"{name} must be a string");
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryReadOptionalString(
        JsonElement parent,
        string name,
        string fallback,
        out string value,
        out DownloadPlanIssue? error)
    {
        value = fallback;
        error = null;

        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = Invalid($"{name} must be a string");
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryReadOptionalInt32(
        JsonElement parent,
        string name,
        int fallback,
        out int value,
        out DownloadPlanIssue? error)
    {
        value = fallback;
        error = null;

        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out value))
        {
            error = Invalid($"{name} must be a 32-bit integer");
            return false;
        }

        return true;
    }

    private static bool TryReadOptionalInt64(
        JsonElement parent,
        string name,
        long fallback,
        out long value,
        out DownloadPlanIssue? error)
    {
        value = fallback;
        error = null;

        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out value))
        {
            error = Invalid($"{name} must be a 64-bit integer");
            return false;
        }

        return true;
    }

    private static bool TryReadOptionalBool(
        JsonElement parent,
        string name,
        bool fallback,
        out bool value,
        out DownloadPlanIssue? error)
    {
        value = fallback;
        error = null;

        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = Invalid($"{name} must be a boolean");
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    private static DownloadPlanIssue Invalid(string message)
        => new(DownloadPlanErrorCode.InvalidPlan, message);
}
