#nullable enable
using System.Collections.Generic;
using System.Text.Json;

namespace Plot.Interpolation
{
    /// <summary>
    /// Normalizes JSON-decoded state into a plain object tree so the path
    /// resolver, lerp helpers and predictor operate on a single uniform shape.
    /// Objects become <see cref="Dictionary{TKey,TValue}"/> of string→object?,
    /// arrays become <see cref="List{T}"/> of object?, numbers become double,
    /// and the JSON primitives map to their CLR equivalents.
    ///
    /// The TS reference works directly on JS objects; in C# the inbound state
    /// arrives as <see cref="JsonElement"/>, so it is normalized once on intake.
    /// </summary>
    public static class StateValue
    {
        /// <summary>
        /// Convert an arbitrary value (typically a <see cref="JsonElement"/>)
        /// into the normalized object tree. Already-normalized values pass
        /// through unchanged.
        /// </summary>
        public static object? Normalize(object? value)
        {
            if (value is JsonElement el) return FromElement(el);
            return value;
        }

        private static object? FromElement(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    var map = new Dictionary<string, object?>();
                    foreach (var prop in el.EnumerateObject())
                    {
                        map[prop.Name] = FromElement(prop.Value);
                    }
                    return map;
                }
                case JsonValueKind.Array:
                {
                    var list = new List<object?>();
                    foreach (var item in el.EnumerateArray())
                    {
                        list.Add(FromElement(item));
                    }
                    return list;
                }
                case JsonValueKind.Number:
                    return el.GetDouble();
                case JsonValueKind.String:
                    return el.GetString();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                default:
                    return null;
            }
        }

        /// <summary>
        /// Try to read a value as a <see cref="double"/>. Returns false when the
        /// value is not numeric.
        /// </summary>
        public static bool TryGetNumber(object? value, out double result)
        {
            switch (value)
            {
                case double d:
                    result = d;
                    return true;
                case float f:
                    result = f;
                    return true;
                case long l:
                    result = l;
                    return true;
                case int i:
                    result = i;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }

        /// <summary>Read a normalized value as a <see cref="Vec2"/>.</summary>
        public static Vec2 AsVec2(object? value)
        {
            var m = AsMap(value);
            return new Vec2(Component(m, "x"), Component(m, "y"));
        }

        /// <summary>Read a normalized value as a <see cref="Vec3"/>.</summary>
        public static Vec3 AsVec3(object? value)
        {
            var m = AsMap(value);
            return new Vec3(Component(m, "x"), Component(m, "y"), Component(m, "z"));
        }

        /// <summary>Read a normalized value as a <see cref="Quat"/>.</summary>
        public static Quat AsQuat(object? value)
        {
            var m = AsMap(value);
            return new Quat(
                Component(m, "x"),
                Component(m, "y"),
                Component(m, "z"),
                Component(m, "w"));
        }

        /// <summary>Read a normalized value as a <see cref="double"/>.</summary>
        public static double AsNumber(object? value)
        {
            return TryGetNumber(value, out var d) ? d : 0;
        }

        /// <summary>Wrap a <see cref="Vec2"/> back into a normalized map.</summary>
        public static Dictionary<string, object?> FromVec2(Vec2 v)
        {
            return new Dictionary<string, object?> { ["x"] = v.X, ["y"] = v.Y };
        }

        /// <summary>Wrap a <see cref="Vec3"/> back into a normalized map.</summary>
        public static Dictionary<string, object?> FromVec3(Vec3 v)
        {
            return new Dictionary<string, object?> { ["x"] = v.X, ["y"] = v.Y, ["z"] = v.Z };
        }

        /// <summary>Wrap a <see cref="Quat"/> back into a normalized map.</summary>
        public static Dictionary<string, object?> FromQuat(Quat q)
        {
            return new Dictionary<string, object?>
            {
                ["x"] = q.X,
                ["y"] = q.Y,
                ["z"] = q.Z,
                ["w"] = q.W,
            };
        }

        /// <summary>
        /// Recursively deep-clone a normalized object tree. Maps and lists are
        /// copied; primitives (immutable) are returned as-is. JsonElement inputs
        /// are normalized first. Used so the predictor never mutates the
        /// authoritative state it was handed.
        /// </summary>
        public static object? DeepClone(object? value)
        {
            switch (value)
            {
                case JsonElement el:
                    return DeepClone(FromElement(el));
                case Dictionary<string, object?> map:
                {
                    var copy = new Dictionary<string, object?>(map.Count);
                    foreach (var kv in map) copy[kv.Key] = DeepClone(kv.Value);
                    return copy;
                }
                case List<object?> list:
                {
                    var copy = new List<object?>(list.Count);
                    foreach (var item in list) copy.Add(DeepClone(item));
                    return copy;
                }
                default:
                    return value;
            }
        }

        /// <summary>
        /// Structural drift magnitude between two normalized values. Ports the
        /// jsonDiffMagnitude in packages/handler-client/src/predictor.ts:
        /// equal → 0; two numbers → abs difference; type mismatch or null
        /// mismatch → 1; non-numeric primitive mismatch → 1; objects → sum of
        /// per-key child magnitudes (missing keys compared against Missing).
        /// </summary>
        public static double JsonDiffMagnitude(object? a, object? b)
        {
            a = Normalize(a);
            b = Normalize(b);
            if (ValuesEqual(a, b)) return 0;
            bool aNum = TryGetNumber(a, out var an);
            bool bNum = TryGetNumber(b, out var bn);
            if (aNum && bNum) return System.Math.Abs(an - bn);
            // typeof a !== typeof b → 1
            if (TypeTag(a) != TypeTag(b)) return 1;
            if (a == null || b == null) return 1;
            if (!(a is Dictionary<string, object?> ao) || !(b is Dictionary<string, object?> bo))
            {
                // Non-object primitives that are not equal.
                return 1;
            }
            double total = 0;
            var keys = new HashSet<string>(ao.Keys);
            foreach (var k in bo.Keys) keys.Add(k);
            foreach (var k in keys)
            {
                object? av = ao.TryGetValue(k, out var avv) ? avv : null;
                object? bv = bo.TryGetValue(k, out var bvv) ? bvv : null;
                total += JsonDiffMagnitude(av, bv);
            }
            return total;
        }

        private static bool ValuesEqual(object? a, object? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (TryGetNumber(a, out var an) && TryGetNumber(b, out var bn)) return an == bn;
            if (a is string sa && b is string sb) return sa == sb;
            if (a is bool ba && b is bool bb) return ba == bb;
            return false;
        }

        // Mirrors JS `typeof` buckets relevant to jsonDiffMagnitude: number,
        // string, boolean, object (includes null per the explicit null guard).
        private static int TypeTag(object? v)
        {
            if (v == null) return 0;
            if (TryGetNumber(v, out _)) return 1;
            if (v is string) return 2;
            if (v is bool) return 3;
            return 4; // object/array
        }

        private static Dictionary<string, object?> AsMap(object? value)
        {
            return value as Dictionary<string, object?> ?? new Dictionary<string, object?>();
        }

        private static double Component(Dictionary<string, object?> map, string key)
        {
            return map.TryGetValue(key, out var v) && TryGetNumber(v, out var d) ? d : 0;
        }
    }
}
