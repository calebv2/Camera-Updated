using System;

namespace ATTSmoothCamera
{
    // Stateless sampling: the caller decides when a path starts, pauses or loops.
    // Keyframe timestamps are seconds from zero, matching CameraLibrary's DTOs.
    internal static class CameraPathSampler
    {
        public static float Duration(CameraPath path)
        {
            Validate(path);
            return path.Points[path.Points.Count - 1].Time;
        }

        public static CameraPoint Sample(CameraPath path, float elapsed)
        {
            float duration = Duration(path);
            if (Single.IsNaN(elapsed) || elapsed <= 0f) return Copy(path.Points[0]);
            if (elapsed >= duration) return Copy(path.Points[path.Points.Count - 1]);

            int left = 0, right = path.Points.Count - 1;
            while (right - left > 1)
            {
                int middle = left + (right - left) / 2;
                if (elapsed < path.Points[middle].Time) right = middle;
                else left = middle;
            }
            CameraPoint a = path.Points[left], b = path.Points[right];
            if (elapsed == a.Time) return Copy(a);
            if (elapsed == b.Time) return Copy(b);
            double u = ((double)elapsed - a.Time) / ((double)b.Time - a.Time);
            CameraPoint result = new CameraPoint {
                Time = elapsed,
                X = Coordinate(path, left, u, 0),
                Y = Coordinate(path, left, u, 1),
                Z = Coordinate(path, left, u, 2),
                Fov = (float)(Fov(a.Fov) + (Fov(b.Fov) - Fov(a.Fov)) * u)
            };
            InterpolateRotation(a, b, u, result);
            return result;
        }

        static void Validate(CameraPath path)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (path.Points == null || path.Points.Count < 2 || path.Points.Count > CameraLibrary.MaxPoints)
                throw new ArgumentException("A camera path needs between 2 and 2048 points.", "path");
            float previous = -1f;
            for (int i = 0; i < path.Points.Count; ++i)
            {
                CameraPoint point = path.Points[i];
                if (point == null || !Finite(point.Time) || point.Time < 0f ||
                    (i == 0 && point.Time != 0f) || (i > 0 && point.Time <= previous))
                    throw new ArgumentException("Camera path times must start at zero and strictly increase.", "path");
                if (!Finite(point.X) || !Finite(point.Y) || !Finite(point.Z) ||
                    !Finite(point.Qx) || !Finite(point.Qy) || !Finite(point.Qz) || !Finite(point.Qw))
                    throw new ArgumentException("Camera path positions and rotations must be finite.", "path");
                if (Norm(point.Qx, point.Qy, point.Qz, point.Qw) < 0.000001)
                    throw new ArgumentException("Camera path rotations must contain valid quaternions.", "path");
                previous = point.Time;
            }
        }

        static CameraPoint Copy(CameraPoint point)
        {
            double norm = Norm(point.Qx, point.Qy, point.Qz, point.Qw);
            return new CameraPoint {
                Time = point.Time, X = point.X, Y = point.Y, Z = point.Z,
                Qx = (float)(point.Qx / norm), Qy = (float)(point.Qy / norm),
                Qz = (float)(point.Qz / norm), Qw = (float)(point.Qw / norm), Fov = (float)Fov(point.Fov)
            };
        }

        static float Coordinate(CameraPath path, int left, double u, int axis)
        {
            CameraPoint a = path.Points[left], b = path.Points[left + 1];
            double start = Axis(a, axis), end = Axis(b, axis);
            if (path.Points.Count == 2) return Float(start + (end - start) * u);
            double span = (double)b.Time - a.Time;
            double u2 = u * u, u3 = u2 * u;
            double value = (2.0 * u3 - 3.0 * u2 + 1.0) * start +
                           (u3 - 2.0 * u2 + u) * span * Tangent(path, left, axis) +
                           (-2.0 * u3 + 3.0 * u2) * end +
                           (u3 - u2) * span * Tangent(path, left + 1, axis);
            return Float(value);
        }

        static double Tangent(CameraPath path, int index, int axis)
        {
            // Multi-point paths ease to rest at both endpoints. Interior
            // derivatives weight neighboring velocities by actual time spans,
            // preserving C1 continuity even with unevenly timed keyframes.
            if (index == 0 || index == path.Points.Count - 1) return 0.0;
            CameraPoint previous = path.Points[index - 1], point = path.Points[index], next = path.Points[index + 1];
            double before = (double)point.Time - previous.Time, after = (double)next.Time - point.Time;
            double incoming = (Axis(point, axis) - Axis(previous, axis)) / before;
            double outgoing = (Axis(next, axis) - Axis(point, axis)) / after;
            return (incoming * after + outgoing * before) / (before + after);
        }

        static void InterpolateRotation(CameraPoint a, CameraPoint b, double u, CameraPoint result)
        {
            double an = Norm(a.Qx, a.Qy, a.Qz, a.Qw), bn = Norm(b.Qx, b.Qy, b.Qz, b.Qw);
            double ax = a.Qx / an, ay = a.Qy / an, az = a.Qz / an, aw = a.Qw / an;
            double bx = b.Qx / bn, by = b.Qy / bn, bz = b.Qz / bn, bw = b.Qw / bn;
            double dot = ax * bx + ay * by + az * bz + aw * bw;
            if (dot < 0.0) { bx = -bx; by = -by; bz = -bz; bw = -bw; dot = -dot; }
            dot = Math.Max(0.0, Math.Min(1.0, dot));
            double wa, wb;
            if (dot > 0.9995) { wa = 1.0 - u; wb = u; }
            else
            {
                double angle = Math.Acos(dot), denominator = Math.Sin(angle);
                wa = Math.Sin((1.0 - u) * angle) / denominator;
                wb = Math.Sin(u * angle) / denominator;
            }
            double x = ax * wa + bx * wb, y = ay * wa + by * wb;
            double z = az * wa + bz * wb, w = aw * wa + bw * wb;
            double length = Norm(x, y, z, w);
            result.Qx = (float)(x / length); result.Qy = (float)(y / length);
            result.Qz = (float)(z / length); result.Qw = (float)(w / length);
        }

        static double Norm(double x, double y, double z, double w) { return Math.Sqrt(x * x + y * y + z * z + w * w); }
        static double Axis(CameraPoint point, int axis) { return axis == 0 ? point.X : axis == 1 ? point.Y : point.Z; }
        static double Fov(float value) { return Finite(value) ? Math.Max(35f, Math.Min(110f, value)) : 75.0; }
        static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        static float Float(double value) { return (float)Math.Max(-Single.MaxValue, Math.Min(Single.MaxValue, value)); }
    }
}
