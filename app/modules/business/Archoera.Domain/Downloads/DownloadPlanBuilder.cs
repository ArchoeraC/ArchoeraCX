namespace Archoera.Domain.Downloads;

/// <summary>
/// Fluent producer for <see cref="DownloadPlan"/>. <see cref="Build"/> computes
/// <c>totalBytes</c> and a deterministic <c>planId</c>, then runs the same
/// validation the Rust executor performs, so an invalid plan never reaches the
/// download pipeline.
/// </summary>
public sealed class DownloadPlanBuilder
{
    private readonly List<PlanFile> _files = [];
    private readonly List<DownloadPostAction> _post = [];
    private string _root;
    private string? _planId;
    private int _concurrency = DownloadPlanLimits.DefaultConcurrency;
    private int _priority;
    private long _bandwidthLimit;
    private DownloadPlanOptions _options = DownloadPlanOptions.Default;

    /// <summary>Starts a plan rooted at an absolute directory.</summary>
    /// <param name="root">Absolute instance/shared root. Existence is checked by the executor.</param>
    public DownloadPlanBuilder(string root)
    {
        _root = root ?? string.Empty;
    }

    /// <summary>Replaces the root (must stay absolute).</summary>
    public DownloadPlanBuilder WithRoot(string root)
    {
        _root = root ?? string.Empty;
        return this;
    }

    /// <summary>Overrides the generated idempotency key.</summary>
    public DownloadPlanBuilder WithPlanId(string? planId)
    {
        _planId = planId;
        return this;
    }

    /// <summary>Sets the desired parallel stream count (1..64).</summary>
    public DownloadPlanBuilder WithConcurrency(int concurrency)
    {
        _concurrency = concurrency;
        return this;
    }

    /// <summary>Sets the cross-plan scheduling priority.</summary>
    public DownloadPlanBuilder WithPriority(int priority)
    {
        _priority = priority;
        return this;
    }

    /// <summary>Sets the bytes/second cap; <c>0</c> means unlimited.</summary>
    public DownloadPlanBuilder WithBandwidthLimit(long bytesPerSecond)
    {
        _bandwidthLimit = bytesPerSecond;
        return this;
    }

    /// <summary>Overrides executor options.</summary>
    public DownloadPlanBuilder WithOptions(DownloadPlanOptions options)
    {
        _options = options ?? DownloadPlanOptions.Default;
        return this;
    }

    /// <summary>Appends a compatibility-only post action (normally none).</summary>
    public DownloadPlanBuilder AddPostAction(DownloadPostAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _post.Add(action);
        return this;
    }

    /// <summary>Appends a file entry.</summary>
    public DownloadPlanBuilder AddFile(PlanFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        _files.Add(file);
        return this;
    }

    /// <summary>Appends a file entry from raw producer fields.</summary>
    public DownloadPlanBuilder AddFile(
        string url,
        string dest,
        DownloadHash hash,
        long size,
        IReadOnlyList<string>? mirrors = null,
        DownloadFileMode mode = DownloadFileMode.Ensure,
        int priority = 0,
        bool executable = false,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(hash);
        _files.Add(PlanFile.Create(
            url,
            dest,
            hash,
            size,
            mirrors,
            mode,
            priority,
            executable,
            headers));
        return this;
    }

    /// <summary>
    /// Appends a file entry from an algorithm/hex pair. The digest is
    /// normalized to lower-case; length is checked by <see cref="Build"/>.
    /// </summary>
    public DownloadPlanBuilder AddFile(
        string url,
        string dest,
        DownloadHashAlgorithm algorithm,
        string hex,
        long size,
        IReadOnlyList<string>? mirrors = null,
        DownloadFileMode mode = DownloadFileMode.Ensure,
        int priority = 0,
        bool executable = false,
        IReadOnlyDictionary<string, string>? headers = null)
        => AddFile(
            url,
            dest,
            DownloadHash.Create(algorithm, hex),
            size,
            mirrors,
            mode,
            priority,
            executable,
            headers);

    /// <summary>
    /// Builds, generates the plan id and validates. Throws
    /// <see cref="DownloadPlanException"/> when the plan would be rejected by
    /// the executor.
    /// </summary>
    public DownloadPlan Build()
    {
        var plan = CreateDraft();
        DownloadPlanValidator.EnsureValid(plan);
        return plan;
    }

    /// <summary>Non-throwing variant of <see cref="Build"/>.</summary>
    public bool TryBuild(out DownloadPlan? plan, out DownloadPlanIssue? issue)
    {
        var draft = CreateDraft();
        issue = DownloadPlanValidator.Validate(draft);
        plan = issue is null ? draft : null;
        return issue is null;
    }

    private DownloadPlan CreateDraft()
    {
        var draft = new DownloadPlan
        {
            SchemaVersion = DownloadPlanLimits.SchemaVersion,
            PlanId = _planId ?? string.Empty,
            Root = _root,
            Concurrency = _concurrency,
            Priority = _priority,
            BandwidthLimit = _bandwidthLimit,
            TotalBytes = DownloadPlanValidator.ComputeTotalBytes(_files),
            Options = _options,
            Files = _files.ToArray(),
            Post = _post.ToArray(),
        };

        return string.IsNullOrEmpty(_planId)
            ? draft with { PlanId = DownloadPlanId.Compute(draft) }
            : draft;
    }
}
