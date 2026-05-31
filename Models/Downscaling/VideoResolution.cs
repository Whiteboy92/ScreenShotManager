namespace ScreenShotManager.Models.Downscaling;

/// <summary>
/// Pixel dimensions of a video stream.
/// </summary>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
public readonly record struct VideoResolution(int Width, int Height)
{
    /// <summary>Largest dimension; used to rank a resolution against the tier table.</summary>
    public int LongestSide => Width >= Height ? Width : Height;

    public override string ToString() => $"{Width}x{Height}";
}
