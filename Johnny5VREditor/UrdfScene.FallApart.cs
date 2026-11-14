using System.Windows.Media.Media3D;

namespace ServoAnimator;

internal sealed partial class UrdfScene
{
    // Display-only override: while active, each moving link's parent joint node
    // shows the baked pose instead of the joint motion. Joint positions keep
    // updating the detached original transforms, so releasing the override
    // restores the live pose exactly.
    private sealed record FallApartJoint(UrdfJoint Joint, Transform3D Original, MatrixTransform3D Override, bool Baked);
    private FallApartClip _fallApartClip;
    private List<FallApartJoint> _fallApartJoints;
    private bool _fallApartLoaded;
    internal bool FallApartActive { get; private set; }

    /// <summary>The model's baked clip, loaded on first use; null when the model has none.</summary>
    internal FallApartClip FallApartClip
    {
        get
        {
            if (_fallApartLoaded) return _fallApartClip;
            _fallApartLoaded = true;
            var clip = FallApartClip.Load(_baseDirectory);
            if (clip == null) return null;
            // Parents before children, so each override can use its parent's final frame.
            var byParent = _joints.Values.ToLookup(j => j.ParentLink, StringComparer.Ordinal);
            var order = new List<FallApartJoint>(); var pending = new Queue<string>(); pending.Enqueue(_rootLinkName);
            while (pending.Count > 0)
                foreach (var joint in byParent[pending.Dequeue()])
                {
                    order.Add(new FallApartJoint(joint, joint.Node.Transform, new MatrixTransform3D(), clip.HasLink(joint.ChildLink)));
                    pending.Enqueue(joint.ChildLink);
                }
            int missing = clip.Links.Count(l => !_parentJointByChild.ContainsKey(l));
            if (missing > 0) System.Diagnostics.Debug.WriteLine($"Fall apart: {missing} clip links are not in this model and are ignored.");
            _fallApartJoints = order; _fallApartClip = clip;
            return clip;
        }
    }

    /// <summary>Show clip frame <paramref name="frame"/>, or release the override with null.</summary>
    internal void SetFallApartFrame(double? frame)
    {
        var clip = FallApartClip;
        if (clip == null) return;
        if (frame == null)
        {
            if (!FallApartActive) return;
            foreach (var item in _fallApartJoints) item.Joint.Node.Transform = item.Original;
            FallApartActive = false; return;
        }
        if (!FallApartActive)
        {
            // Every node is detached, including frame-only links without a baked pose, so
            // joint motion upstream of a baked part cannot leak into the display.
            foreach (var item in _fallApartJoints) item.Joint.Node.Transform = item.Override;
            FallApartActive = true;
            ClearCollisionState();
        }
        double weight = FallApartTimeline.BlendWeight(frame.Value, clip.ShudderFrames);
        // Content frames (link transform, then everything above it) for the live pose and the displayed pose.
        var live = new Dictionary<string, Matrix3D>(StringComparer.Ordinal);
        var shown = new Dictionary<string, Matrix3D>(StringComparer.Ordinal);
        var root = LinkMatrix(_rootLinkName); live[_rootLinkName] = root; shown[_rootLinkName] = root;
        foreach (var item in _fallApartJoints)
        {
            var joint = item.Joint;
            if (!live.TryGetValue(joint.ParentLink, out var parentLive)) continue;
            var parentShown = shown[joint.ParentLink];
            var liveFrame = item.Original.Value * parentLive;
            Matrix3D shownFrame;
            if (item.Baked)
            {
                shownFrame = FallApartMath.Blend(liveFrame, clip.Sample(joint.ChildLink, frame.Value), weight);
                var inverse = parentShown;
                if (!inverse.HasInverse) continue;
                inverse.Invert();
                item.Override.Matrix = shownFrame * inverse;
            }
            else
            {
                item.Override.Matrix = item.Original.Value;     // frozen until the next frame update
                shownFrame = item.Original.Value * parentShown;
            }
            var link = LinkMatrix(joint.ChildLink);
            live[joint.ChildLink] = link * liveFrame; shown[joint.ChildLink] = link * shownFrame;
        }
    }

    private Matrix3D LinkMatrix(string name) =>
        _links.TryGetValue(name, out var link) && link.Transform != null ? link.Transform.Value : Matrix3D.Identity;
}
