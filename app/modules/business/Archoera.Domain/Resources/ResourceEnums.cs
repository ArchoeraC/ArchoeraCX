namespace Archoera.Domain.Resources;

/// <summary>Content kinds this client can install into an instance.</summary>
public enum ResourceKind
{
    Mod,
    Shader,
    ResourcePack,
}

/// <summary>Modrinth version channel.</summary>
public enum ResourceVersionType
{
    Release,
    Beta,
    Alpha,
}

/// <summary>Dependency relationship reported by a provider.</summary>
public enum ResourceDependencyKind
{
    Required,
    Optional,
    Incompatible,
    Embedded,
}

/// <summary>Search ordering.</summary>
public enum ResourceSort
{
    Relevance,
    Downloads,
    Follows,
    Newest,
    Updated,
}

/// <summary>Wire names for resource DTOs. Modrinth names are the canonical form.</summary>
public static class ResourceWire
{
    public static string ToWire(this ResourceKind kind) => kind switch
    {
        ResourceKind.Mod => "mod",
        ResourceKind.Shader => "shader",
        ResourceKind.ResourcePack => "resourcepack",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParseKind(string? value, out ResourceKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "mod":
                kind = ResourceKind.Mod;
                return true;
            case "shader" or "shaders":
                kind = ResourceKind.Shader;
                return true;
            case "resourcepack" or "resource_pack" or "resource-pack":
                kind = ResourceKind.ResourcePack;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static string ToWire(this ResourceVersionType type) => type switch
    {
        ResourceVersionType.Release => "release",
        ResourceVersionType.Beta => "beta",
        ResourceVersionType.Alpha => "alpha",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static bool TryParseVersionType(string? value, out ResourceVersionType type)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "release":
                type = ResourceVersionType.Release;
                return true;
            case "beta":
                type = ResourceVersionType.Beta;
                return true;
            case "alpha":
                type = ResourceVersionType.Alpha;
                return true;
            default:
                type = default;
                return false;
        }
    }

    public static string ToWire(this ResourceDependencyKind kind) => kind switch
    {
        ResourceDependencyKind.Required => "required",
        ResourceDependencyKind.Optional => "optional",
        ResourceDependencyKind.Incompatible => "incompatible",
        ResourceDependencyKind.Embedded => "embedded",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParseDependencyKind(string? value, out ResourceDependencyKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "required":
                kind = ResourceDependencyKind.Required;
                return true;
            case "optional":
                kind = ResourceDependencyKind.Optional;
                return true;
            case "incompatible":
                kind = ResourceDependencyKind.Incompatible;
                return true;
            case "embedded":
                kind = ResourceDependencyKind.Embedded;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static string ToWire(this ResourceSort sort) => sort switch
    {
        ResourceSort.Relevance => "relevance",
        ResourceSort.Downloads => "downloads",
        ResourceSort.Follows => "follows",
        ResourceSort.Newest => "newest",
        ResourceSort.Updated => "updated",
        _ => throw new ArgumentOutOfRangeException(nameof(sort)),
    };

    public static bool TryParseSort(string? value, out ResourceSort sort)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "relevance":
                sort = ResourceSort.Relevance;
                return true;
            case "downloads":
                sort = ResourceSort.Downloads;
                return true;
            case "follows":
                sort = ResourceSort.Follows;
                return true;
            case "newest":
                sort = ResourceSort.Newest;
                return true;
            case "updated":
                sort = ResourceSort.Updated;
                return true;
            default:
                sort = default;
                return false;
        }
    }
}
