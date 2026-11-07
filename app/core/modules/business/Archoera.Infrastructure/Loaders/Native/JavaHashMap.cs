namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>
/// Java <c>HashMap</c> iteration order (capacity 16, 0.75 load factor, no
/// treeification for the installer data). srgutils and binarypatcher 1.1.x use
/// it when they iterate their maps, which leaks into the merged mapping's
/// overload order and into the order of classes created by binary patches.
/// </summary>
internal static class JavaHashMap
{
    public static IEnumerable<string> Order(IReadOnlyList<string> insertionOrder)
    {
        if (insertionOrder.Count == 0)
        {
            return [];
        }

        var capacity = 16;
        var threshold = 12;
        var size = 0;
        foreach (var _ in insertionOrder)
        {
            size++;
            if (size > threshold)
            {
                capacity *= 2;
                threshold = capacity * 3 / 4;
            }
        }

        var buckets = new List<string>[capacity];
        foreach (var key in insertionOrder)
        {
            var hash = StringHash(key);
            var bucket = (hash ^ (int)((uint)hash >> 16)) & (capacity - 1);
            (buckets[bucket] ??= []).Add(key);
        }

        var result = new List<string>(insertionOrder.Count);
        foreach (var bucket in buckets)
        {
            if (bucket is not null)
            {
                result.AddRange(bucket);
            }
        }

        return result;
    }

    public static int StringHash(string value)
    {
        unchecked
        {
            var hash = 0;
            foreach (var character in value)
            {
                hash = 31 * hash + character;
            }

            return hash;
        }
    }
}
