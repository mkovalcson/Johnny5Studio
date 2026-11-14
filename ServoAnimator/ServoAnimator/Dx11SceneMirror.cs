using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using H = HelixToolkit.Wpf.SharpDX;
using D = HelixToolkit.SharpDX;
using Color4 = HelixToolkit.Maths.Color4;

namespace ServoAnimator;

/// <summary>Mirrors the existing model graph without changing animation, collision or URDF semantics.
/// Geometry and materials are cached, and only changed graph nodes are synchronized.</summary>
internal sealed class Dx11SceneMirror : IDisposable
{
    internal H.GroupModel3D Root { get; } = new();
    private readonly HashSet<Link> _dirty = new();
    private readonly Dictionary<MeshGeometry3D, MeshEntry> _meshes = new();
    private readonly Dictionary<Material, MaterialEntry> _materials = new();
    private readonly HashSet<MeshEntry> _dirtyMeshes = new();
    private readonly HashSet<MaterialEntry> _dirtyMaterials = new();
    private Link _root;
    internal int MeshCount => _meshes.Count;
    internal int MaterialCount => _materials.Count;
    internal long GeometryUpdates { get; private set; }
    internal long MaterialUpdates { get; private set; }

    internal void Synchronize(Model3D source)
    {
        if (!ReferenceEquals(source, _root?.Source))
        {
            Root.Children.Clear(); _root?.Dispose(); ClearCaches();
            _root = source == null ? null : new Link(this, source);
            if (_root != null) Root.Children.Add(_root.Element);
        }
        // A geometry/material change can propagate through the WPF graph; coalesce it once per frame.
        foreach (var link in _dirty.ToArray()) if (!link.Disposed) link.Update();
        _dirty.Clear();
        foreach (var mesh in _dirtyMeshes.ToArray()) mesh.Update();
        _dirtyMeshes.Clear();
        foreach (var material in _dirtyMaterials.ToArray()) material.Update();
        _dirtyMaterials.Clear();
        // Rebuilt iris meshes and transient effect materials must not accumulate indefinitely.
        foreach (var pair in _meshes.Where(p => p.Value.Users == 0 && (!p.Key.IsFrozen || _meshes.Count > 256)).ToArray())
        { pair.Value.Dispose(); _meshes.Remove(pair.Key); }
        foreach (var pair in _materials.Where(p => p.Value.Consumers.Count == 0 && (!p.Key.IsFrozen || _materials.Count > 128)).ToArray())
        { pair.Value.Dispose(); _materials.Remove(pair.Key); }
    }
    private MeshEntry Mesh(MeshGeometry3D source)
    {
        if (!_meshes.TryGetValue(source, out var entry)) _meshes.Add(source, entry = new(this, source));
        return entry;
    }
    private MaterialEntry Material(Material source)
    {
        if (source == null) return null;
        if (!_materials.TryGetValue(source, out var entry)) _materials.Add(source, entry = new(this, source));
        return entry;
    }
    private void ClearCaches()
    {
        foreach (var entry in _meshes.Values) entry.Dispose();
        foreach (var entry in _materials.Values) entry.Dispose();
        _meshes.Clear(); _materials.Clear(); _dirty.Clear(); _dirtyMeshes.Clear(); _dirtyMaterials.Clear();
    }
    public void Dispose() { Root.Children.Clear(); _root?.Dispose(); _root = null; ClearCaches(); Root.SceneNode.Dispose(); }

    private sealed class Link : IDisposable
    {
        private readonly Dx11SceneMirror _owner;
        internal Model3D Source { get; }
        internal H.Element3D Element { get; }
        internal bool Disposed { get; private set; }
        private List<Link> _children = new();
        private readonly H.MeshGeometryModel3D _front, _back;
        private MaterialEntry _frontMaterial, _backMaterial;
        private MeshEntry _mesh;
        internal Link(Dx11SceneMirror owner, Model3D source)
        {
            _owner = owner; Source = source;
            Element = source switch
            {
                AmbientLight => new H.AmbientLight3D(),
                DirectionalLight => new H.DirectionalLight3D(),
                SpotLight => new H.SpotLight3D(),
                PointLight => new H.PointLight3D(),
                _ => new H.GroupModel3D()
            };
            Element.IsHitTestVisible = false;
            if (source is GeometryModel3D)
            {
                _front = new() { FrontCounterClockwise = true, IsHitTestVisible = false };
                _back = new() { FrontCounterClockwise = true, CullMode = SharpDX.Direct3D11.CullMode.Front, InvertNormal = true, IsHitTestVisible = false };
                ((H.GroupModel3D)Element).Children.Add(_front); ((H.GroupModel3D)Element).Children.Add(_back);
            }
            if (!source.IsFrozen) source.Changed += Changed;
            Update();
        }
        private void Changed(object sender, EventArgs args) => _owner._dirty.Add(this);
        internal void Update()
        {
            if (!ReferenceEquals(Element.Transform, Source.Transform)) Element.Transform = Source.Transform;
            switch (Source)
            {
                case Model3DGroup group:
                    if (_children.Count == group.Children.Count && _children.Select(c => c.Source).SequenceEqual(group.Children)) break;
                    var old = _children.ToList(); var next = new List<Link>();
                    foreach (var child in group.Children)
                    {
                        var link = old.FirstOrDefault(c => ReferenceEquals(c.Source, child));
                        if (link != null) old.Remove(link); else link = new Link(_owner, child);
                        next.Add(link);
                    }
                    var target = (H.GroupModel3D)Element; target.Children.Clear();
                    foreach (var child in next) target.Children.Add(child.Element);
                    foreach (var child in old) child.Dispose();
                    _children = next;
                    break;
                case GeometryModel3D geometry:
                    var mesh = geometry.Geometry as MeshGeometry3D;
                    var nextMesh = mesh == null ? null : _owner.Mesh(mesh);
                    if (!ReferenceEquals(_mesh, nextMesh))
                    {
                        if (_mesh != null) _mesh.Users--;
                        _mesh = nextMesh; if (_mesh != null) _mesh.Users++;
                        _front.Geometry = _back.Geometry = _mesh?.Geometry;
                    }
                    SetMaterial(_front, geometry.Material, ref _frontMaterial);
                    SetMaterial(_back, geometry.BackMaterial, ref _backMaterial);
                    bool shared = geometry.Material != null && ReferenceEquals(geometry.Material, geometry.BackMaterial);
                    _front.CullMode = shared ? SharpDX.Direct3D11.CullMode.None : SharpDX.Direct3D11.CullMode.Back;
                    _front.IsRendering = geometry.Material != null;
                    _back.IsRendering = geometry.BackMaterial != null && !shared;
                    break;
                case AmbientLight ambient: ((H.AmbientLight3D)Element).Color = LightColor(ambient.Color); break;
                case DirectionalLight directional:
                    var light = (H.DirectionalLight3D)Element; light.Color = LightColor(directional.Color); light.Direction = directional.Direction; break;
                case PointLight point:
                    var p = (H.PointLight3D)Element; p.Color = LightColor(point.Color); p.Position = point.Position; p.Range = point.Range;
                    p.Attenuation = new(point.ConstantAttenuation, point.LinearAttenuation, point.QuadraticAttenuation); break;
                case SpotLight spot:
                    var s = (H.SpotLight3D)Element; s.Color = LightColor(spot.Color); s.Position = spot.Position; s.Direction = spot.Direction;
                    s.Range = spot.Range; s.InnerAngle = spot.InnerConeAngle; s.OuterAngle = spot.OuterConeAngle;
                    s.Attenuation = new(spot.ConstantAttenuation, spot.LinearAttenuation, spot.QuadraticAttenuation); break;
                default: throw new NotSupportedException("DX11 does not support scene node " + Source.GetType().Name);
            }
        }
        // WPF light intensities are linear scRGB; Helix's Color conversion uses byte RGB directly.
        private static Color LightColor(Color color) => Color.FromArgb(color.A,
            (byte)Math.Clamp(Math.Round(color.ScR * 255), 0, 255),
            (byte)Math.Clamp(Math.Round(color.ScG * 255), 0, 255),
            (byte)Math.Clamp(Math.Round(color.ScB * 255), 0, 255));
        private void SetMaterial(H.MeshGeometryModel3D mesh, Material source, ref MaterialEntry entry)
        {
            var next = _owner.Material(source);
            if (!ReferenceEquals(entry, next))
            {
                entry?.Consumers.Remove(mesh); entry = next; entry?.Consumers.Add(mesh); mesh.Material = entry?.Material;
            }
            mesh.IsTransparent = entry?.Transparent == true;
        }
        public void Dispose()
        {
            if (Disposed) return; Disposed = true;
            if (!Source.IsFrozen) Source.Changed -= Changed;
            _owner._dirty.Remove(this);
            foreach (var child in _children) child.Dispose(); _children.Clear();
            if (_front != null) { _frontMaterial?.Consumers.Remove(_front); _backMaterial?.Consumers.Remove(_back); }
            if (_mesh != null) _mesh.Users--;
            if (Element is H.GroupModel3D group) group.Children.Clear();
            Element.Transform = Transform3D.Identity; Element.SceneNode.Dispose();
            _front?.SceneNode.Dispose(); _back?.SceneNode.Dispose();
        }
    }
    private sealed class MeshEntry : IDisposable
    {
        private readonly Dx11SceneMirror _owner;
        private readonly MeshGeometry3D _source;
        internal D.MeshGeometry3D Geometry { get; } = new();
        internal int Users;
        internal MeshEntry(Dx11SceneMirror owner, MeshGeometry3D source)
        { _owner = owner; _source = source; if (!source.IsFrozen) source.Changed += Changed; Update(); }
        private void Changed(object s, EventArgs e) => _owner._dirtyMeshes.Add(this);
        internal void Update()
        {
            Geometry.Positions = new HelixToolkit.Vector3Collection(_source.Positions.Select(p => new Vector3((float)p.X, (float)p.Y, (float)p.Z)));
            Geometry.Indices = new HelixToolkit.IntCollection(_source.TriangleIndices);
            var normals = _source.Normals.Select(n => new Vector3((float)n.X, (float)n.Y, (float)n.Z)).ToArray();
            if (normals.Length != _source.Positions.Count)
            {
                normals = new Vector3[_source.Positions.Count]; var positions = Geometry.Positions;
                for (int i = 0; i + 2 < Geometry.Indices.Count; i += 3)
                {
                    int a = Geometry.Indices[i], b = Geometry.Indices[i + 1], c = Geometry.Indices[i + 2];
                    var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                    normals[a] += normal; normals[b] += normal; normals[c] += normal;
                }
                for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].LengthSquared() > 1e-16 ? Vector3.Normalize(normals[i]) : Vector3.UnitZ;
            }
            Geometry.Normals = new HelixToolkit.Vector3Collection(normals);
            Geometry.TextureCoordinates = new HelixToolkit.Vector2Collection(_source.TextureCoordinates.Select(p => new Vector2((float)p.X, (float)p.Y)));
            _owner.GeometryUpdates++;
        }
        public void Dispose() { if (!_source.IsFrozen) _source.Changed -= Changed; }
    }
    private sealed class MaterialEntry : IDisposable
    {
        private readonly Dx11SceneMirror _owner;
        private readonly Material _source;
        internal H.PhongMaterial Material { get; } = new();
        internal bool Transparent { get; private set; }
        internal HashSet<H.MeshGeometryModel3D> Consumers { get; } = new();
        internal MaterialEntry(Dx11SceneMirror owner, Material source)
        { _owner = owner; _source = source; if (!source.IsFrozen) source.Changed += Changed; Update(); }
        private void Changed(object s, EventArgs e) => _owner._dirtyMaterials.Add(this);
        internal void Update()
        {
            Color4 diffuse = new(0, 0, 0, 0), emissive = new(0, 0, 0, 0), specular = new(0, 0, 0, 0);
            D.TextureModel diffuseMap = null, emissiveMap = null; float shininess = 32;
            bool translucentMap = false;
            void Visit(Material source)
            {
                if (source is MaterialGroup group) { foreach (var child in group.Children) Visit(child); return; }
                Brush brush = source switch { DiffuseMaterial d => d.Brush, EmissiveMaterial e => e.Brush, SpecularMaterial s => s.Brush, _ => throw new NotSupportedException("Unsupported DX11 material: " + source.GetType().Name) };
                if (brush == null) return;
                var color = brush is SolidColorBrush solid ? ToColor(solid.Color, solid.Opacity) : new Color4(1, 1, 1, 1);
                if (source is DiffuseMaterial) { diffuse = color; if (brush is not SolidColorBrush) { diffuseMap = Texture(brush); translucentMap |= !OpaqueImage(brush); } }
                else if (source is EmissiveMaterial) { emissive = color; if (brush is not SolidColorBrush) { emissiveMap = Texture(brush); translucentMap |= !OpaqueImage(brush); } }
                else if (source is SpecularMaterial spec) { specular = color; shininess = (float)spec.SpecularPower; }
            }
            Visit(_source);
            float alpha = Math.Max(diffuse.Alpha, emissive.Alpha);
            diffuse.Alpha = alpha;
            Material.DiffuseColor = diffuse; Material.AmbientColor = diffuse;
            void ApplyTextureAmbient(Material source)
            {
                if (source is MaterialGroup group) { foreach (var child in group.Children) ApplyTextureAmbient(child); }
                else if (source is DiffuseMaterial { Brush: ImageBrush { ImageSource: BitmapSource bitmap } }
                    && UrdfWeathering.TryTextureLighting(bitmap, out var ambient))
                    Material.AmbientColor = ToColor(ambient, 1);
            }
            ApplyTextureAmbient(_source);
            Material.EmissiveColor = emissive; Material.SpecularColor = specular; Material.SpecularShininess = shininess;
            Material.DiffuseMap = diffuseMap; Material.EmissiveMap = emissiveMap;
            Transparent = alpha < .999f || translucentMap;
            foreach (var consumer in Consumers) consumer.IsTransparent = Transparent;
            _owner.MaterialUpdates++;
        }
        private static Color4 ToColor(Color c, double opacity) => new(c.ScR, c.ScG, c.ScB, (float)(c.ScA * opacity));
        private static bool OpaqueImage(Brush brush) => brush is ImageBrush image && image.Opacity == 1
            && image.ImageSource is BitmapSource bitmap && image.Transform.Value.IsIdentity && image.RelativeTransform.Value.IsIdentity
            && image.Stretch == Stretch.Fill && (bitmap.Format == PixelFormats.Bgr32 || bitmap.Format == PixelFormats.Bgr24 || bitmap.Format == PixelFormats.Rgb24);
        private static D.TextureModel Texture(Brush brush)
        {
            BitmapSource image;
            if (brush is ImageBrush ib && ib.ImageSource is BitmapSource bitmap && brush.Opacity == 1 && brush.Transform.Value.IsIdentity && brush.RelativeTransform.Value.IsIdentity)
                image = bitmap;
            else
            {
                var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(brush, null, new Rect(0, 0, 512, 512));
                var raster = new RenderTargetBitmap(512, 512, 96, 96, PixelFormats.Pbgra32); raster.Render(visual); image = raster;
            }
            bool srgb = UrdfWeathering.TryTextureLighting(image, out _);
            if (image.Format != PixelFormats.Bgra32) image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = image.PixelWidth * 4; var bytes = new byte[stride * image.PixelHeight]; image.CopyPixels(bytes, stride, 0);
            return new D.TextureModel(bytes, srgb ? SharpDX.DXGI.Format.B8G8R8A8_UNorm_SRgb : SharpDX.DXGI.Format.B8G8R8A8_UNorm, image.PixelWidth, image.PixelHeight);
        }
        public void Dispose() { if (!_source.IsFrozen) _source.Changed -= Changed; Consumers.Clear(); }
    }
}
