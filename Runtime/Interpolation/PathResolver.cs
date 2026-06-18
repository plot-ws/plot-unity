#nullable enable
using System;
using System.Collections.Generic;

namespace Plot.Interpolation
{
    /// <summary>A resolved leaf path with its values from the A and B states.</summary>
    public struct ResolvedLeaf
    {
        public string Path;
        public object? ValueA;
        public object? ValueB;

        public ResolvedLeaf(string path, object? valueA, object? valueB)
        {
            Path = path;
            ValueA = valueA;
            ValueB = valueB;
        }
    }

    /// <summary>
    /// Resolves dot-delimited paths with a single-level <c>*</c> wildcard over
    /// the normalized state tree. Ports
    /// packages/client/src/interpolation/path.ts.
    ///
    /// A missing segment yields <see cref="Missing"/> — distinct from a present
    /// JSON null — so callers can mirror the TS "undefined" branches exactly.
    /// </summary>
    public static class PathResolver
    {
        /// <summary>Sentinel for a path segment that does not exist.</summary>
        public static readonly object Missing = new object();

        private static object? Walk(object? state, IReadOnlyList<string> segments)
        {
            object? cur = state;
            foreach (var seg in segments)
            {
                if (cur is Dictionary<string, object?> map)
                {
                    cur = map.TryGetValue(seg, out var v) ? v : Missing;
                }
                else
                {
                    return Missing;
                }
            }
            return cur;
        }

        public static List<ResolvedLeaf> Resolve(string pattern, object? a, object? b)
        {
            var segs = pattern.Split('.');
            int wildcardCount = 0;
            int star = -1;
            for (int i = 0; i < segs.Length; i++)
            {
                if (segs[i] == "*")
                {
                    wildcardCount++;
                    if (star == -1) star = i;
                }
            }
            if (wildcardCount > 1)
            {
                throw new ArgumentException(
                    $"PathResolver: multi-wildcard patterns are not supported: {pattern}");
            }

            var result = new List<ResolvedLeaf>();
            if (star == -1)
            {
                result.Add(new ResolvedLeaf(pattern, Walk(a, segs), Walk(b, segs)));
                return result;
            }

            var head = new List<string>();
            for (int i = 0; i < star; i++) head.Add(segs[i]);
            var tail = new List<string>();
            for (int i = star + 1; i < segs.Length; i++) tail.Add(segs[i]);

            var aParent = Walk(a, head);
            var bParent = Walk(b, head);
            var aMap = aParent as Dictionary<string, object?>;
            var bMap = bParent as Dictionary<string, object?>;

            var keys = new SortedSet<string>(StringComparer.Ordinal);
            if (aMap != null)
            {
                foreach (var k in aMap.Keys) keys.Add(k);
            }
            if (bMap != null)
            {
                foreach (var k in bMap.Keys) keys.Add(k);
            }

            foreach (var k in keys)
            {
                var childPathParts = new List<string>(head) { k };
                childPathParts.AddRange(tail);
                string childPath = string.Join(".", childPathParts);
                object? aChild = aMap != null && aMap.TryGetValue(k, out var av) ? av : Missing;
                object? bChild = bMap != null && bMap.TryGetValue(k, out var bv) ? bv : Missing;
                result.Add(new ResolvedLeaf(
                    childPath,
                    Walk(aChild, tail),
                    Walk(bChild, tail)));
            }
            return result;
        }
    }
}
