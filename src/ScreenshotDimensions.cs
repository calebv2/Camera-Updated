using System;

namespace ATTSmoothCamera
{
    // Numeric policy shared by the real photo service and its Unity-free tests.
    internal static class ScreenshotDimensions
    {
        const int MaximumSide = 7680;
        const long MaximumPixels = 33177600L; // Approximately 33 MP.

        public static void Calculate(int desktopWidth, int desktopHeight, int scale,
                                     out int width, out int height)
        {
            int multiplier = Math.Max(1, Math.Min(4, scale));
            double desiredWidth = (double)Math.Max(1, desktopWidth) * multiplier;
            double desiredHeight = (double)Math.Max(1, desktopHeight) * multiplier;
            double factor = Math.Min(1.0, Math.Min(MaximumSide / desiredWidth, MaximumSide / desiredHeight));
            double pixels = desiredWidth * desiredHeight;
            factor = Math.Min(factor, Math.Sqrt(MaximumPixels / pixels));
            width = Math.Max(1, (int)Math.Floor(desiredWidth * factor));
            height = Math.Max(1, (int)Math.Floor(desiredHeight * factor));
        }
    }
}
