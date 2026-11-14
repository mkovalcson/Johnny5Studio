using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

internal sealed partial class UrdfScene
{
    // Beam effect: every robot visual temporarily shares one material whose colors are animated (one shared brush
    // per channel, so a frame update is two property changes regardless of model size). The originals are restored
    // from _originalMaterials, the same record the collision highlighting uses.
    private readonly SolidColorBrush _beamDiffuse = new(Colors.Transparent), _beamEmissive = new(Colors.Black);
    private MaterialGroup _beamMaterial;
    internal bool BeamActive { get; private set; }

    /// <summary>Show the robot as a transporter shimmer: <paramref name="opacity"/> 0..1 of a cyan-white body plus a
    /// <paramref name="glow"/> 0..1 emissive blue-white.</summary>
    internal void SetBeamLook(double opacity, double glow)
    {
        _beamMaterial ??= new MaterialGroup { Children = { new DiffuseMaterial(_beamDiffuse), new EmissiveMaterial(_beamEmissive) } };
        _beamDiffuse.Color = Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), 205, 232, 255);
        double g = Math.Clamp(glow, 0, 1);
        _beamEmissive.Color = Color.FromArgb((byte)Math.Round(g * 255), (byte)(120 + 100 * g), (byte)(190 + 50 * g), 255);
        if (BeamActive) return;
        BeamActive = true;
        ClearCollisionState();
        foreach (var visual in _linkVisuals.Values.SelectMany(v => v))
        {
            visual.Material = _beamMaterial;
            if (visual.BackMaterial != null) visual.BackMaterial = _beamMaterial;
        }
    }

    internal void ClearBeamLook()
    {
        if (!BeamActive) return;
        BeamActive = false;
        foreach (var visual in _linkVisuals.Values.SelectMany(v => v))
            if (_originalMaterials.TryGetValue(visual, out var original)) { visual.Material = original.Front; visual.BackMaterial = original.Back; }
    }
}
