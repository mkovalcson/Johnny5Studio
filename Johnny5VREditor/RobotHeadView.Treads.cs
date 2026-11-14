using System.Windows;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private double _treadForwardMetres, _treadTurnDegrees;
    private bool _treadsDirty;
    private double _casterAngle, _casterTarget;
    private double _casterWheelLeft,_casterWheelRight;
    private double _casterPendingForward, _casterPendingTurn;
    internal double CasterTurnRadius => _scene?.CasterDistanceBehind(NeckBaseScreenAnchor.X) ?? .5;
    internal double CasterTrail => _scene?.CasterTrail ?? 0;
    private bool _casterTimelineOwned;
    private TimeSpan? _casterLastFrame;
    internal void SetTimelineTreads(double forwardMetres, double casterAngle, double casterTarget,double wheelLeft=0,double wheelRight=0)
    {
        _casterTimelineOwned = true;
        _casterPendingForward = _casterPendingTurn = 0;
        if (_casterAngle != casterAngle||_casterWheelLeft!=wheelLeft||_casterWheelRight!=wheelRight) _treadsDirty = true;
        _casterAngle = casterAngle; _casterTarget = casterTarget;
        _casterWheelLeft=wheelLeft;_casterWheelRight=wheelRight;
        double turn = _modelSpinDegrees + (_stage?.HeadingDegrees ?? 0);
        if (_treadForwardMetres == forwardMetres && _treadTurnDegrees == turn) return;
        _treadForwardMetres = forwardMetres; _treadTurnDegrees = turn; _treadsDirty = true;
    }
    private void AdvanceTreads(double forwardMetres, double turnDegrees)
    {
        if (forwardMetres == 0 && turnDegrees == 0) return;
        _casterTimelineOwned = false;
        // Combine translation and spin before choosing the steering angle. Their
        // callbacks can arrive separately during the same rendered frame.
        _casterPendingForward += forwardMetres; _casterPendingTurn += turnDegrees;
        _treadForwardMetres += forwardMetres; _treadTurnDegrees += turnDegrees; _treadsDirty = true;
    }
    private void RenderTreadFrame(TimeSpan frameTime)
    {
        double before=_casterAngle;
        double seconds = _casterLastFrame.HasValue ? Math.Clamp((frameTime - _casterLastFrame.Value).TotalSeconds, 0, .15) : 0;
        _casterLastFrame = frameTime;
        double trail = CasterTrail;
        if (!_casterTimelineOwned && trail > 0)
        {
            // A trailing caster is rolled round by the motion itself, through the full circle.
            double rolled = CasterSwivel.Roll(_casterAngle, _casterPendingForward, _casterPendingTurn, CasterTurnRadius, trail);
            _casterTarget = CasterSwivel.TravelDirection(_casterPendingForward, _casterPendingTurn, CasterTurnRadius) ?? _casterTarget;
            if (rolled != _casterAngle) { _casterAngle = rolled; _treadsDirty = true; }
        }
        else if (!_casterTimelineOwned)
            _casterTarget = CasterSwivel.Target(_casterTarget, _casterPendingForward, _casterPendingTurn, CasterTurnRadius);
        if (!VrEditorMode.Active&&(!IsVisible || Window.GetWindow(this)?.WindowState == WindowState.Minimized)) return;
        if (!_casterTimelineOwned && trail <= 0 && _casterAngle != _casterTarget)
        {
            _casterAngle = CasterSwivel.Advance(_casterAngle, _casterTarget, seconds); _treadsDirty = true;
        }
        if(!_casterTimelineOwned)
        {
            var wheels=Johnny5.VrBridge.CasterWheelTravel.Step(before,_casterAngle,_casterPendingForward,_casterPendingTurn,CasterTurnRadius,.034875);
            _casterWheelLeft+=wheels.Left;_casterWheelRight+=wheels.Right;
        }
        _casterPendingForward = _casterPendingTurn = 0;
        RenderTreads();
    }
    private void RenderTreads()
    {
        if (!_treadsDirty || _scene == null || !VrEditorMode.Active&&(!IsVisible || Window.GetWindow(this)?.WindowState == WindowState.Minimized)) return;
        var belts = DifferentialDrive.BeltTravel(_treadForwardMetres, _treadTurnDegrees, _scene.TreadGauge);
        // Positive Z rotation turns left: the left belt reverses and right belt advances.
        _scene.SetTreadTravel(belts.Left, belts.Right);
        _scene.SetCasterSwivel(_casterAngle,_casterWheelLeft,_casterWheelRight);
        _treadsDirty = false;
    }
}
