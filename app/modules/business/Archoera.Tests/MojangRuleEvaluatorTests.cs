using Archoera.Domain.Mojang;

namespace Archoera.Tests;

public sealed class MojangRuleEvaluatorTests
{
    [Fact]
    public void No_rules_allows_every_platform()
    {
        Assert.True(MojangRuleEvaluator.IsAllowed(null, MojangPlatform.LinuxX64));
        Assert.True(MojangRuleEvaluator.IsAllowed([], MojangPlatform.WindowsX64));
    }

    [Fact]
    public void Rules_default_to_deny_when_nothing_matches()
    {
        MojangRule[] rules =
        [
            new(MojangRuleAction.Disallow, new MojangRuleOs("windows", null, null)),
        ];

        Assert.False(MojangRuleEvaluator.IsAllowed(rules, MojangPlatform.LinuxX64));
        Assert.False(MojangRuleEvaluator.IsAllowed(rules, MojangPlatform.WindowsX64));
    }

    [Fact]
    public void Allow_rules_select_the_matching_os()
    {
        MojangRule[] rules = [MojangTestData.AllowOs("linux")];

        Assert.True(MojangRuleEvaluator.IsAllowed(rules, MojangPlatform.LinuxX64));
        Assert.False(MojangRuleEvaluator.IsAllowed(rules, MojangPlatform.WindowsX64));
        Assert.False(MojangRuleEvaluator.IsAllowed(rules, MojangPlatform.MacOsX64));
    }

    [Fact]
    public void Last_matching_rule_wins()
    {
        MojangRule[] allowThenDisallow =
        [
            MojangTestData.AllowOs("linux"),
            new(MojangRuleAction.Disallow, new MojangRuleOs("linux", null, null)),
        ];

        MojangRule[] disallowThenAllow =
        [
            new(MojangRuleAction.Disallow, new MojangRuleOs("linux", null, null)),
            MojangTestData.AllowOs("linux"),
        ];

        Assert.False(MojangRuleEvaluator.IsAllowed(allowThenDisallow, MojangPlatform.LinuxX64));
        Assert.True(MojangRuleEvaluator.IsAllowed(disallowThenAllow, MojangPlatform.LinuxX64));
    }

    [Fact]
    public void Arch_rules_compare_normalized_aliases()
    {
        MojangRule[] x86 = [MojangTestData.AllowOs(null, "x86")];
        MojangRule[] x64 = [MojangTestData.AllowOs(null, "x86_64")];
        MojangRule[] arm64 = [MojangTestData.AllowOs(null, "aarch64")];

        var linuxX86 = MojangPlatform.Create(MojangSystemOs.Linux, MojangSystemArch.X86);

        Assert.True(MojangRuleEvaluator.IsAllowed(x86, linuxX86));
        Assert.False(MojangRuleEvaluator.IsAllowed(x86, MojangPlatform.LinuxX64));
        Assert.True(MojangRuleEvaluator.IsAllowed(x64, MojangPlatform.LinuxX64));
        Assert.False(MojangRuleEvaluator.IsAllowed(x64, MojangPlatform.MacOsArm64));
        Assert.True(MojangRuleEvaluator.IsAllowed(arm64, MojangPlatform.MacOsArm64));
    }

    [Fact]
    public void Feature_rules_require_an_exact_flag_value()
    {
        MojangRule[] customResolution =
        [
            new(
                MojangRuleAction.Allow,
                Os: null,
                Features: new Dictionary<string, bool>
                {
                    [MojangFeatures.HasCustomResolution] = true,
                }),
        ];

        var withoutFeature = MojangPlatform.LinuxX64;
        var withFeature = MojangPlatform.Create(
            MojangSystemOs.Linux,
            MojangSystemArch.X64,
            features: new Dictionary<string, bool>
            {
                [MojangFeatures.HasCustomResolution] = true,
            });

        Assert.False(MojangRuleEvaluator.IsAllowed(customResolution, withoutFeature));
        Assert.True(MojangRuleEvaluator.IsAllowed(customResolution, withFeature));
    }

    [Fact]
    public void Os_version_rules_use_a_regex_and_require_a_version()
    {
        MojangRule[] windows7 = [new(
            MojangRuleAction.Allow,
            new MojangRuleOs("windows", null, "^6\\.1\\."))];

        var windows7Platform = MojangPlatform.Create(
            MojangSystemOs.Windows,
            MojangSystemArch.X64,
            osVersion: "6.1.7601");

        var unknownVersion = MojangPlatform.WindowsX64;

        Assert.True(MojangRuleEvaluator.IsAllowed(windows7, windows7Platform));
        Assert.False(MojangRuleEvaluator.IsAllowed(windows7, unknownVersion));
    }

    [Fact]
    public void Macos_alias_matches_osx_rules()
    {
        MojangRule[] rules = [MojangTestData.AllowOs("macos")];

        Assert.True(MojangRuleEvaluator.IsAllowed(rules, MojangPlatform.MacOsX64));
        Assert.False(MojangRuleEvaluator.IsAllowed(rules, MojangPlatform.LinuxX64));
    }
}
