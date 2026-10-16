namespace ArgoBooks.Helpers;

/// <summary>Timing shared by the controls that animate frame by frame on a timer.</summary>
internal static class FrameAnimation
{
    /// <summary>About 60 frames a second.</summary>
    public const double FrameIntervalMs = 16.0;

    /// <summary>Cubic ease-in-out of progress t from 0 to 1.</summary>
    public static double CubicInOut(double t) =>
        t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
}
