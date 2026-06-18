#nullable enable
using System;
using System.Collections.Generic;

namespace Plot.Interpolation
{
    /// <summary>
    /// Minimal RFC 6902 JSON Patch applier over the normalized state tree. The
    /// server emits patches via fast-json-patch's <c>compare</c>; this applies
    /// them to the locally tracked state for <c>state-patch</c> envelopes.
    ///
    /// Supports add, remove, replace, move, copy and test, with paths resolved
    /// per RFC 6901 (including the "-" array append token and ~0/~1 escapes).
    /// Mutates and returns the supplied (already-cloned) document.
    /// </summary>
    public static class JsonPatch
    {
        public static object? Apply(object? document, object? patch)
        {
            var ops = StateValue.Normalize(patch) as List<object?>;
            if (ops == null) return document;
            object? doc = document;
            foreach (var rawOp in ops)
            {
                if (!(StateValue.Normalize(rawOp) is Dictionary<string, object?> op)) continue;
                string? kind = op.TryGetValue("op", out var k) ? k as string : null;
                string path = op.TryGetValue("path", out var p) ? p as string ?? "" : "";
                switch (kind)
                {
                    case "add":
                        doc = AddOrReplace(doc, ParsePath(path), op.TryGetValue("value", out var av) ? av : null, isAdd: true);
                        break;
                    case "replace":
                        doc = AddOrReplace(doc, ParsePath(path), op.TryGetValue("value", out var rv) ? rv : null, isAdd: false);
                        break;
                    case "remove":
                        doc = Remove(doc, ParsePath(path));
                        break;
                    case "move":
                    {
                        string from = op.TryGetValue("from", out var f) ? f as string ?? "" : "";
                        var moved = Get(doc, ParsePath(from));
                        doc = Remove(doc, ParsePath(from));
                        doc = AddOrReplace(doc, ParsePath(path), moved, isAdd: true);
                        break;
                    }
                    case "copy":
                    {
                        string from = op.TryGetValue("from", out var f) ? f as string ?? "" : "";
                        var copied = StateValue.DeepClone(Get(doc, ParsePath(from)));
                        doc = AddOrReplace(doc, ParsePath(path), copied, isAdd: true);
                        break;
                    }
                    case "test":
                    {
                        var actual = Get(doc, ParsePath(path));
                        var expected = op.TryGetValue("value", out var ev) ? StateValue.Normalize(ev) : null;
                        if (StateValue.JsonDiffMagnitude(actual, expected) != 0)
                        {
                            throw new InvalidOperationException($"JsonPatch: test failed at {path}");
                        }
                        break;
                    }
                }
            }
            return doc;
        }

        private static List<string> ParsePath(string path)
        {
            var segs = new List<string>();
            if (string.IsNullOrEmpty(path)) return segs;
            // RFC 6901: leading '/', segments separated by '/', ~1 → '/', ~0 → '~'.
            var parts = path.Split('/');
            for (int i = 1; i < parts.Length; i++)
            {
                segs.Add(parts[i].Replace("~1", "/").Replace("~0", "~"));
            }
            return segs;
        }

        private static object? Get(object? doc, List<string> segs)
        {
            object? cur = doc;
            foreach (var seg in segs)
            {
                if (cur is Dictionary<string, object?> map)
                {
                    cur = map.TryGetValue(seg, out var v) ? v : null;
                }
                else if (cur is List<object?> list && int.TryParse(seg, out var idx) && idx >= 0 && idx < list.Count)
                {
                    cur = list[idx];
                }
                else
                {
                    return null;
                }
            }
            return cur;
        }

        private static object? AddOrReplace(object? doc, List<string> segs, object? value, bool isAdd)
        {
            value = StateValue.Normalize(value);
            if (segs.Count == 0) return value; // replace whole document
            object? parent = Get(doc, segs.GetRange(0, segs.Count - 1));
            string last = segs[segs.Count - 1];
            if (parent is Dictionary<string, object?> map)
            {
                map[last] = value;
            }
            else if (parent is List<object?> list)
            {
                if (last == "-")
                {
                    list.Add(value);
                }
                else if (int.TryParse(last, out var idx))
                {
                    if (isAdd)
                    {
                        if (idx < 0) idx = 0;
                        if (idx > list.Count) idx = list.Count;
                        list.Insert(idx, value);
                    }
                    else if (idx >= 0 && idx < list.Count)
                    {
                        list[idx] = value;
                    }
                }
            }
            return doc;
        }

        private static object? Remove(object? doc, List<string> segs)
        {
            if (segs.Count == 0) return null;
            object? parent = Get(doc, segs.GetRange(0, segs.Count - 1));
            string last = segs[segs.Count - 1];
            if (parent is Dictionary<string, object?> map)
            {
                map.Remove(last);
            }
            else if (parent is List<object?> list && int.TryParse(last, out var idx) && idx >= 0 && idx < list.Count)
            {
                list.RemoveAt(idx);
            }
            return doc;
        }
    }
}
