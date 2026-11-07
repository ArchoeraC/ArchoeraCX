using Archoera.Domain.Downloads;

namespace Archoera.Tests;

public sealed class DownloadPlanCombinerTests
{
    private static string Root(string name)
        => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archoera-combine", name));

    private static string Sha1(char value) => new(value, 40);

    private static DownloadPlan Plan(
        string root,
        string dest,
        string hash,
        long size,
        string name = "https://example.invalid/file.jar",
        int concurrency = 8)
        => new DownloadPlanBuilder(root)
            .WithConcurrency(concurrency)
            .AddFile(
                name,
                dest,
                DownloadHashAlgorithm.Sha1,
                hash,
                size)
            .Build();

    [Fact]
    public void Combine_deduplicates_identical_destinations_and_keeps_max_concurrency()
    {
        var root = Root("same");
        var vanilla = Plan(root, "libraries/a.jar", Sha1('a'), 10, "https://a.invalid/a.jar", 4);
        var loader = Plan(root, "libraries/a.jar", Sha1('a'), 10, "https://b.invalid/a.jar", 16);

        var combined = DownloadPlanCombiner.Combine([vanilla, loader]);

        var file = Assert.Single(combined.Files);
        Assert.Equal("libraries/a.jar", file.Dest);
        Assert.Equal(10, combined.TotalBytes);
        Assert.Equal(16, combined.Concurrency);
        Assert.Equal("https://a.invalid/a.jar", file.Url);
        Assert.True(DownloadPlanValidator.Validate(combined) is null);
    }

    [Fact]
    public void Combine_rejects_conflicting_bytes_for_the_same_destination()
    {
        var root = Root("conflict");
        var first = Plan(root, "libraries/a.jar", Sha1('a'), 10);
        var second = Plan(root, "libraries/a.jar", Sha1('b'), 11);

        var exception = Assert.Throws<DownloadPlanException>(
            () => DownloadPlanCombiner.Combine([first, second]));

        Assert.Equal(DownloadPlanErrorCode.DuplicateDest, exception.Code);
    }

    [Fact]
    public void Combine_rejects_plans_with_different_roots()
    {
        var first = Plan(Root("root-a"), "a.jar", Sha1('a'), 1);
        var second = Plan(Root("root-b"), "b.jar", Sha1('b'), 1);

        var exception = Assert.Throws<DownloadPlanException>(
            () => DownloadPlanCombiner.Combine([first, second]));

        Assert.Equal(DownloadPlanErrorCode.InvalidPlan, exception.Code);
    }

    [Fact]
    public void Combine_orders_files_by_destination_and_is_deterministic()
    {
        var root = Root("order");
        var first = Plan(root, "z.jar", Sha1('a'), 1);
        var second = Plan(root, "a.jar", Sha1('b'), 1);

        var left = DownloadPlanCombiner.Combine([first, second]);
        var right = DownloadPlanCombiner.Combine([second, first]);

        Assert.Equal(["a.jar", "z.jar"], left.Files.Select(file => file.Dest));
        Assert.Equal(left.PlanId, right.PlanId);
        Assert.Equal(left.Files.Select(file => file.Dest), right.Files.Select(file => file.Dest));
    }
}
