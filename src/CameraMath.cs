using System;

namespace ATTSmoothCamera
{
    internal static class CameraMath
    {
        private const double LogTwo = 0.69314718055994530942;

        // The caller owns any pause/frame-time cap. This exponential preserves
        // the same convergence over equal elapsed time at different frame rates.
        // Invalid/nonpositive half lives mean immediate convergence; invalid or
        // nonpositive elapsed times mean no movement.
        internal static float Blend(float halfLife, float dt)
        {
            if (!Finite(dt) || dt <= 0f) return 0f;
            if (!Finite(halfLife) || halfLife <= 0f) return 1f;
            double value = 1.0 - Math.Exp(-LogTwo * (double)dt / halfLife);
            if (value <= 0.0) return 0f;
            if (value >= 1.0) return 1f;
            return (float)value;
        }

        // The threshold is a distance, while the measured distance is squared.
        // An invalid measurement requests a reset; callers must validate the
        // target pose itself before placing the camera.
        internal static bool ShouldSnap(float distanceSquared, float threshold, bool targetChanged)
        {
            if (targetChanged || !Finite(distanceSquared) || distanceSquared < 0f) return true;
            if (!Finite(threshold) || threshold < 0f) threshold = 0f;
            double limit = (double)threshold * threshold;
            return (double)distanceSquared > limit;
        }

        // Keep orbit state bounded without losing a valid large elapsed time.
        // Double intermediates avoid overflow for any finite float inputs.
        internal static float OrbitAngle(float degrees, float speed, float dt)
        {
            double angle = Finite(degrees) ? degrees : 0.0;
            if (Finite(speed) && Finite(dt) && dt > 0f)
                angle += (double)speed * dt;
            angle %= 360.0;
            if (angle < 0.0) angle += 360.0;
            float result = (float)angle;
            // Rounding a value just below 360 can produce 360 as a float.
            return result >= 360f ? 0f : result;
        }

        private static bool Finite(float value)
        {
            return !Single.IsNaN(value) && !Single.IsInfinity(value);
        }
    }
}
