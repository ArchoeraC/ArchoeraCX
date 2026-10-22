namespace Archoera.Domain.Downloads;

/// <summary>
/// Pre-flight plan validation, fail-fast and in the same order as
/// <c>validate_plan_inner</c>. Callers can use <see cref="Validate"/> to render
/// an error message or <see cref="EnsureValid"/> to throw.
/// </summary>
public static class DownloadPlanValidator
{
    private const string HeaderAllowlistHint =
        "only 'authorization'/'cookie' headers are accepted by the executor";

    /// <summary>Returns the first issue, or <see langword="null"/> when valid.</summary>
    public static DownloadPlanIssue? Validate(DownloadPlan? plan)
    {
        if (plan is null)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "plan is required");
        }

        if (plan.SchemaVersion != DownloadPlanLimits.SchemaVersion)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"unsupported schemaVersion {plan.SchemaVersion} " +
                $"(expected {DownloadPlanLimits.SchemaVersion})");
        }

        if (string.IsNullOrWhiteSpace(plan.PlanId))
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "planId is required");
        }

        if (plan.PlanId.Length > DownloadPlanLimits.MaxPlanIdLength)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "planId is too long");
        }

        if (plan.Concurrency is < DownloadPlanLimits.MinConcurrency
            or > DownloadPlanLimits.MaxConcurrency)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"concurrency {plan.Concurrency} is outside " +
                $"{DownloadPlanLimits.MinConcurrency}..={DownloadPlanLimits.MaxConcurrency}");
        }

        if (plan.BandwidthLimit < 0)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "bandwidthLimit must not be negative");
        }

        if (plan.Options.PerHostConcurrency is < 0 or > DownloadPlanLimits.MaxPerHostConcurrency)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"options.perHostConcurrency {plan.Options.PerHostConcurrency} is outside " +
                $"0..={DownloadPlanLimits.MaxPerHostConcurrency}");
        }

        if (plan.Options.Proxy is { } proxy)
        {
            if (!Uri.TryCreate(proxy.Url, UriKind.Absolute, out var proxyUri) ||
                proxyUri.Scheme is not ("http" or "https" or "socks4" or "socks4a"
                    or "socks5" or "socks5h") ||
                string.IsNullOrEmpty(proxyUri.Host))
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    "options.proxy.url must be an http(s)/socks URL with a host");
            }

            if (proxy.NoProxy.Count > 256 || proxy.NoProxy.Any(entry => entry.Length > 255))
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    "options.proxy.noProxy entries are too long or too many");
            }
        }

        var rootIssue = DownloadPlanPaths.ValidateRoot(plan.Root);
        if (rootIssue is not null)
        {
            return rootIssue;
        }

        if (plan.Files is null)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "files is required");
        }

        if (plan.Files.Count > DownloadPlanLimits.MaxFiles)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.PlanTooLarge,
                $"{plan.Files.Count} files exceeds limit {DownloadPlanLimits.MaxFiles}");
        }

        var optionsIssue = ValidateOptions(plan.Options);
        if (optionsIssue is not null)
        {
            return optionsIssue;
        }

        if (plan.Post is null)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "post is required");
        }

        var destinations = new HashSet<string>(DestinationComparer);
        var totalBytes = 0L;

        foreach (var file in plan.Files)
        {
            if (file is null)
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    "files[] contains a null entry");
            }

            if (file.Hash is null)
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidHash,
                    $"{file.Dest}: hash is required");
            }

            if (!file.Hash.IsCanonicalHex())
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidHash,
                    $"{file.Dest}: {file.Hash.Algo} hash must be " +
                    $"{file.Hash.ExpectedHexLength} lower-case hex characters");
            }

            if (file.Size < 0)
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    $"{file.Dest}: size must not be negative");
            }

            if (file.Size > DownloadPlanLimits.MaxFileBytes)
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.PlanTooLarge,
                    $"{file.Dest}: size {file.Size} exceeds 1 TiB");
            }

            if (file.Size > long.MaxValue - totalBytes)
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.PlanTooLarge,
                    "sum of file sizes overflows");
            }

            totalBytes += file.Size;

            var destIssue = DownloadPlanPaths.TryNormalizeDest(file.Dest, out var normalizedDest);
            if (destIssue is not null)
            {
                return destIssue;
            }

            if (!string.Equals(normalizedDest, file.Dest, StringComparison.Ordinal))
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    $"{file.Dest}: dest is not normalized");
            }

            if (!destinations.Add(normalizedDest))
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.DuplicateDest,
                    file.Dest);
            }

            if (file.Mode is DownloadFileMode.Link or DownloadFileMode.Symlink)
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    $"{file.Dest}: mode '{file.Mode.ToWire()}' is not supported by this executor build");
            }

            var urlIssue = DownloadPlanPaths.TryValidateUrl(file.Url, out var primary);
            if (urlIssue is not null)
            {
                return urlIssue;
            }

            if (file.Mirrors is not null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal)
                {
                    primary!.AbsoluteUri,
                };

                foreach (var mirror in file.Mirrors)
                {
                    var mirrorIssue = DownloadPlanPaths.TryValidateUrl(mirror, out var mirrorUri);
                    if (mirrorIssue is not null)
                    {
                        return mirrorIssue;
                    }

                    if (!seen.Add(mirrorUri!.AbsoluteUri))
                    {
                        return new DownloadPlanIssue(
                            DownloadPlanErrorCode.InvalidPlan,
                            $"{file.Dest}: duplicate mirror '{mirror}'");
                    }
                }
            }

            var headerIssue = ValidateHeaders(file);
            if (headerIssue is not null)
            {
                return headerIssue;
            }
        }

        if (plan.TotalBytes < 0)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "totalBytes must not be negative");
        }

        return null;
    }

    /// <summary>Throws <see cref="DownloadPlanException"/> for the first issue.</summary>
    public static void EnsureValid(DownloadPlan? plan)
    {
        var issue = Validate(plan);
        if (issue is not null)
        {
            throw DownloadPlanException.FromIssue(issue);
        }
    }

    /// <summary>Sum of <c>files[].size</c>, saturating instead of overflowing.</summary>
    public static long ComputeTotalBytes(IReadOnlyList<PlanFile>? files)
    {
        if (files is null)
        {
            return 0;
        }

        var total = 0L;
        foreach (var file in files)
        {
            if (file is null || file.Size <= 0)
            {
                continue;
            }

            total = file.Size > long.MaxValue - total ? long.MaxValue : total + file.Size;
        }

        return total;
    }

    private static DownloadPlanIssue? ValidateOptions(DownloadPlanOptions? options)
    {
        if (options is null)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "options is required");
        }

        if (options.RangeThreshold < 0)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "options.rangeThreshold must not be negative");
        }

        if (options.ChunkSize < 0)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "options.chunkSize must not be negative");
        }

        if (options.ProgressIntervalMs < 0)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "options.progressIntervalMs must not be negative");
        }

        return null;
    }

    private static DownloadPlanIssue? ValidateHeaders(PlanFile file)
    {
        if (file.Headers is null)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"{file.Dest}: headers must not be null");
        }

        foreach (var (name, value) in file.Headers)
        {
            if (string.IsNullOrEmpty(name) || name.Length > DownloadPlanLimits.MaxHeaderNameLength)
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    $"{file.Dest}: invalid header name");
            }

            if (!string.Equals(name, "authorization", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, "cookie", StringComparison.OrdinalIgnoreCase))
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    $"{file.Dest}: header '{name}' is rejected; {HeaderAllowlistHint}");
            }

            if (value is null || value.Length > DownloadPlanLimits.MaxHeaderValueLength)
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    $"{file.Dest}: header '{name}' is too large");
            }

            foreach (var character in value)
            {
                if (character < 0x20 || character == 0x7f)
                {
                    return new DownloadPlanIssue(
                        DownloadPlanErrorCode.InvalidPlan,
                        $"{file.Dest}: header '{name}' contains control characters");
                }
            }
        }

        return null;
    }

    private static StringComparer DestinationComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
