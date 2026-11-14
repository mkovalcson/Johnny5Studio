using System.Numerics;

namespace Johnny5VR;

internal static partial class CurvedSurfaces
{
    internal static bool IsRoundedEyeSurface(string source) => Path.GetFileName(source?.Replace('\\', '/'))?.ToLowerInvariant() is
        "eye_tube_left.stl" or "eye_tube_right.stl" or "eye_gimbal_a0a0a0.stl" or "eye_gimbal_spacers_cad1ee.stl"
        or "eye_fixed_f6f6f3.stl" or "eye_fixed_cad1ee_rest_without_gimbal_spacers_or_side_screws.stl"
        or "eye_lens_cad1ee_without_gimbal_screws.stl" or "eye_lens_f6f6f3.stl"
        or "eye_lens_outer_circumferences_black.stl" or "iris_gold_continuous.stl" or "iris_inner_black_band.stl";

    internal static bool IsRoundedHeadSurface(string source)
    {
        if (IsRoundedEyeSurface(source)) return true;
        string path = source?.Replace('\\', '/').ToLowerInvariant() ?? "";
        string name = Path.GetFileName(path);
        if(path.Contains("/body/",StringComparison.Ordinal))
            return name is "shoulder_cad_sleeve_left.stl" or "shoulder_cad_sleeve_right.stl"
                or "shoulder_cad_rim_left_bottom.stl" or "shoulder_cad_rim_right_bottom.stl"
                or "shoulder_cad_rim_left_top.stl" or "shoulder_cad_rim_right_top.stl"
                or "shoulder_cad_bump_left.stl" or "shoulder_cad_bump_right.stl"
                or "shoulder_cad_bump_panel_left.stl" or "shoulder_cad_bump_panel_right.stl"
                or "shoulder_cad_hinge_dome_left.stl" or "shoulder_cad_hinge_dome_right.stl";
        // The upper sockets are grouped into the mouth's silver mesh; the
        // lower pair share the neck's silver mesh. Keep their cap/rim creases.
        if (path.EndsWith("/simplemouth/static_e9e9eb.stl", StringComparison.Ordinal)) return true;
        if (path.Contains("/neckassembly/", StringComparison.Ordinal) && name is
            "neck_static_black.stl" or "neck_bellows_black.stl" or "neck_rigid_support.stl" or "neck_static_cad_e9e9eb.stl") return true;
        if (path.EndsWith("/fasteners/stainless_socket_head.step.stl", StringComparison.Ordinal)) return true;
        if (path.EndsWith("/fasteners/stainless_button_socket_head.step.stl", StringComparison.Ordinal)) return true;
        if (path.EndsWith("/rearcover/rear_cover.stl", StringComparison.Ordinal)) return true;
        // CAD static meshes are grouped by material. Restrict these names to
        // the head shell folder; mouth and body meshes reuse the same names.
        if (!path.Contains("/simplifiedhead2/", StringComparison.Ordinal)) return false;
        return name is "static_020202.stl" or "static_080b4e.stl" or "static_737373.stl"
            or "static_97a3da.stl" or "static_f6f4e9.stl"
            or "nose_body.stl" or "nose_basket.stl"
            or "upper_left_flap_panel.stl" or "upper_right_flap_panel.stl"
            or "lower_left_flap_panel.stl" or "lower_right_flap_panel.stl"
            or "upper_left_carrier.stl" or "upper_right_carrier.stl"
            or "vent_left_drive.stl" or "vent_right_drive.stl"
            or "vent_left_fin1.stl" or "vent_left_fin2.stl" or "vent_left_fin3.stl" or "vent_left_fin4.stl" or "vent_left_fin5.stl"
            or "vent_right_fin1.stl" or "vent_right_fin2.stl" or "vent_right_fin3.stl" or "vent_right_fin4.stl" or "vent_right_fin5.stl";
    }

}
