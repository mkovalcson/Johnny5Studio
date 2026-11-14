using System.IO;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

internal static partial class PrimitiveMeshes
{
    // Apply only to the supplied robots' selected curved visuals, never collision
    // meshes or unrelated CAD parts. STL carries face normals, not smooth ones.
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

    internal static MeshGeometry3D SmoothSurfaceNormals(MeshGeometry3D source, double creaseDegrees = 35)
    {
        var mesh = source.Clone();
        var positions = mesh.Positions;
        var indices = mesh.TriangleIndices;
        var faceNormals = new Vector3D[indices.Count / 3];
        var corners = new Dictionary<(long X, long Y, long Z), List<(int Face, double Weight)>>();
        // CAD STL coordinates are single precision. Weld coincident corners for
        // shading only, to 0.1 micrometre; retain every original vertex/triangle.
        static (long, long, long) Key(Point3D p) => ((long)Math.Round(p.X * 1e7), (long)Math.Round(p.Y * 1e7), (long)Math.Round(p.Z * 1e7));
        for (int face = 0; face < faceNormals.Length; face++)
        {
            int offset = face * 3;
            var a = positions[indices[offset]]; var b = positions[indices[offset + 1]]; var c = positions[indices[offset + 2]];
            var normal = Vector3D.CrossProduct(b - a, c - a);
            if (normal.LengthSquared < 1e-24)
            {
                // Tiny CAD fillet slivers can collapse at STL precision. Keep
                // their supplied normal instead of introducing a zero vector.
                normal = source.Normals.Count == positions.Count ? source.Normals[indices[offset]] : new Vector3D(0, 0, 1);
                if (normal.LengthSquared > 1e-24) normal.Normalize(); else normal = new Vector3D(0, 0, 1);
                faceNormals[face] = normal;
                continue;
            }
            normal.Normalize(); faceNormals[face] = normal;
            for (int corner = 0; corner < 3; corner++)
            {
                var p = positions[indices[offset + corner]];
                var u = positions[indices[offset + (corner + 1) % 3]] - p;
                var v = positions[indices[offset + (corner + 2) % 3]] - p;
                u.Normalize(); v.Normalize();
                double angle = Math.Acos(Math.Clamp(Vector3D.DotProduct(u, v), -1, 1));
                var key = Key(p);
                if (!corners.TryGetValue(key, out var neighbors)) corners[key] = neighbors = new();
                neighbors.Add((face, angle));
            }
        }
        var normals = new Vector3DCollection(positions.Count);
        for (int i = 0; i < positions.Count; i++) normals.Add(new Vector3D());
        double threshold = Math.Cos(creaseDegrees * Math.PI / 180);
        for (int face = 0; face < faceNormals.Length; face++)
        for (int corner = 0; corner < 3; corner++)
        {
            int vertex = indices[face * 3 + corner];
            var normal = faceNormals[face]; var sum = new Vector3D();
            if (corners.TryGetValue(Key(positions[vertex]), out var neighbors))
                foreach (var neighbor in neighbors)
                    if (Vector3D.DotProduct(normal, faceNormals[neighbor.Face]) >= threshold)
                        sum += faceNormals[neighbor.Face] * neighbor.Weight;
            // Corner-angle weights avoid shading ripples from uneven CAD
            // triangulation. Creases keep caps, holes and sharp rims crisp.
            if (sum.LengthSquared > 1e-24) { sum.Normalize(); normals[vertex] = sum; }
            else normals[vertex] = normal;
        }
        mesh.Normals = normals;
        mesh.Freeze(); return mesh;
    }
}
