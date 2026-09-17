using Dango.Core.Animation;

namespace Dango.Desktop.Physics.Presentation.Attachments;

public sealed record EyeMotionSample(AttachmentPoint Gaze, double Openness, EyeShape Shape, double Strength);
public sealed record EyePresentationFrame(AttachmentPoint Center, double Width, double Height, double Angle,
    double Openness, EyeShape Shape, double Strength, bool Visible, bool IsRight = false);
public sealed record FaceLayout(EyePresentationFrame Left, EyePresentationFrame Right,
    double GazeScale, double SpacingScale, double SizeScale, string Safety);
public sealed record AttachmentPresentationFrame(long StepIndex, int Generation, ShapeTilt ShapeTilt, FaceLayout Face)
{
    public HatMotionFrame? Hat { get; init; }
}

/// <summary>Shared eye contour for safety and drawing, in equal-scale reference-width units.</summary>
public static class EyeOutline
{
    public const int Samples = 128;
    // Artwork renders the same polygon vertices. Only the exact neutral ellipse differs:
    // radius <= 0.0375, 128 chords: r * (1 - cos(pi / 128)) < 0.000012.
    public const double ChordError = 0.0001;
    public static IReadOnlyList<AttachmentPoint> Create(EyePresentationFrame eye)
    {
        return Array.AsReadOnly(EyeArtwork.Create(eye).Fills.SelectMany(p => p).ToArray());
    }
}
