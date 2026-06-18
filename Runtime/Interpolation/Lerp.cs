#nullable enable
using System;

namespace Plot.Interpolation
{
    /// <summary>2D vector matching the JSON-decoded { x, y } shape.</summary>
    public struct Vec2
    {
        public double X;
        public double Y;

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>3D vector matching the JSON-decoded { x, y, z } shape.</summary>
    public struct Vec3
    {
        public double X;
        public double Y;
        public double Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>Quaternion matching the JSON-decoded { x, y, z, w } shape.</summary>
    public struct Quat
    {
        public double X;
        public double Y;
        public double Z;
        public double W;

        public Quat(double x, double y, double z, double w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }

    /// <summary>
    /// Component-wise interpolation helpers. Ports
    /// packages/client/src/interpolation/lerp/* faithfully.
    /// </summary>
    public static class Lerp
    {
        private const double DotThreshold = 0.9995;

        public static double LerpNumber(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        public static Vec2 LerpVec2(Vec2 a, Vec2 b, double t)
        {
            return new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        public static Vec3 LerpVec3(Vec3 a, Vec3 b, double t)
        {
            return new Vec3(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t);
        }

        /// <summary>Spherical linear interpolation along the short path.</summary>
        public static Quat LerpQuat(Quat a, Quat b, double t)
        {
            double dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            double bx = b.X, by = b.Y, bz = b.Z, bw = b.W;
            if (dot < 0)
            {
                dot = -dot;
                bx = -bx;
                by = -by;
                bz = -bz;
                bw = -bw;
            }

            // Endpoint short-circuits — the trigonometric formulas below carry
            // float noise, so return endpoints exactly. For dot<0 the short-path
            // destination is the negated b; both represent the same rotation.
            if (t <= 0) return new Quat(a.X, a.Y, a.Z, a.W);
            if (t >= 1) return new Quat(bx, by, bz, bw);

            if (dot > DotThreshold)
            {
                // Linear lerp of two unit quats drifts off the unit sphere; restore.
                return Normalize(new Quat(
                    a.X + (bx - a.X) * t,
                    a.Y + (by - a.Y) * t,
                    a.Z + (bz - a.Z) * t,
                    a.W + (bw - a.W) * t));
            }

            double theta0 = Math.Acos(dot);
            double sinTheta0 = Math.Sin(theta0);
            double theta = theta0 * t;
            double sinTheta = Math.Sin(theta);
            double s0 = Math.Cos(theta) - dot * sinTheta / sinTheta0;
            double s1 = sinTheta / sinTheta0;
            return new Quat(
                s0 * a.X + s1 * bx,
                s0 * a.Y + s1 * by,
                s0 * a.Z + s1 * bz,
                s0 * a.W + s1 * bw);
        }

        private static Quat Normalize(Quat q)
        {
            double len = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            if (len == 0) throw new InvalidOperationException("cannot normalize a zero quaternion");
            return new Quat(q.X / len, q.Y / len, q.Z / len, q.W / len);
        }
    }
}
