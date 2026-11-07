using System.IO.Compression;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Minimal class hierarchy used to resolve inherited member mappings. ART's
/// EnhancedRemapper searches the superclass chain and interfaces when a member
/// reference is not declared by the owner itself.
/// </summary>
internal sealed class ClassHierarchy
{
    private readonly Dictionary<string, (string? Super, string[] Interfaces)> _classes =
        new(StringComparer.Ordinal);

    public static ClassHierarchy Empty { get; } = new();

    public int Count => _classes.Count;

    public static ClassHierarchy FromJar(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var archive = ZipFile.OpenRead(path);
        var hierarchy = new ClassHierarchy();
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".class", StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var model = ClassFileReader.Read(memory.ToArray());
            var super = model.SuperClass == 0
                ? null
                : model.Pool.GetClassName(model.SuperClass);
            var interfaces = model.Interfaces
                .Select(index => model.Pool.GetClassName(index))
                .ToArray();
            hierarchy._classes[model.ClassName] = (super, interfaces);
        }

        return hierarchy;
    }

    /// <summary>
    /// Candidate classes for field resolution, in JVMS 5.4.3.2 order: the
    /// superinterfaces (depth first, declaration order) are searched before the
    /// superclass chain.
    /// </summary>
    public IEnumerable<string> FieldAncestors(string name) => Search(name, interfacesFirst: true);

    /// <summary>
    /// Candidate classes for method resolution, in JVMS 5.4.3.3 order: the
    /// superclass chain is searched before the superinterfaces.
    /// </summary>
    public IEnumerable<string> MethodAncestors(string name) => Search(name, interfacesFirst: false);

    private IEnumerable<string> Search(string name, bool interfacesFirst)
    {
        if (!_classes.TryGetValue(name, out var start))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal) { name };
        if (interfacesFirst)
        {
            foreach (var candidate in VisitInterfaces(start, seen))
            {
                yield return candidate;
            }

            foreach (var candidate in VisitSuper(start, seen))
            {
                yield return candidate;
            }
        }
        else
        {
            foreach (var candidate in VisitSuper(start, seen))
            {
                yield return candidate;
            }

            foreach (var candidate in VisitInterfaces(start, seen))
            {
                yield return candidate;
            }
        }
    }

    private IEnumerable<string> VisitInterfaces(
        (string? Super, string[] Interfaces) entry,
        HashSet<string> seen)
    {
        foreach (var interfaceName in entry.Interfaces)
        {
            if (!seen.Add(interfaceName))
            {
                continue;
            }

            yield return interfaceName;
            if (_classes.TryGetValue(interfaceName, out var nested))
            {
                foreach (var candidate in VisitInterfaces(nested, seen))
                {
                    yield return candidate;
                }

                foreach (var candidate in VisitSuper(nested, seen))
                {
                    yield return candidate;
                }
            }
        }
    }

    private IEnumerable<string> VisitSuper(
        (string? Super, string[] Interfaces) entry,
        HashSet<string> seen)
    {
        if (entry.Super is null || !seen.Add(entry.Super))
        {
            yield break;
        }

        yield return entry.Super;
        if (_classes.TryGetValue(entry.Super, out var nested))
        {
            foreach (var candidate in VisitInterfaces(nested, seen))
            {
                yield return candidate;
            }

            foreach (var candidate in VisitSuper(nested, seen))
            {
                yield return candidate;
            }
        }
    }
}
