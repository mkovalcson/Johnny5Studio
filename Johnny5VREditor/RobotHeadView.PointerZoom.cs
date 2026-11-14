using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private bool _adjustingPointerZoom;

    internal bool ZoomAtPointer(Point pointer,int delta)
    {
        if(delta==0)return false;
        double distance=Math.Clamp(_cameraDistance*Math.Pow(delta>0?.88:1.14,Math.Abs((double)delta)/120),.55,11.5);
        if(Math.Abs(distance-_cameraDistance)<1e-12)return false;
        bool anchorValid=TryPointerZoomAnchor(pointer,out var anchor)&&!_tvState.Active;
        _cameraDistance=distance;
        _adjustingPointerZoom=true;
        try
        {
            UpdateCamera();
            // The existing target includes neck-base anchoring, fitted static
            // images and stage following. Solve their real projection rather
            // than assuming zoom changes only distance to a fixed target.
            for(int iteration=0;anchorValid&&iteration<5;iteration++)
            {
                if(!ProjectZoomAnchor(anchor,out var current))break;
                var error=pointer-current;if(error.Length<.02)break;
                double h=_frameHorizontalPercent,v=_frameVerticalPercent;
                double dh=h<199.75?.25:-.25,dv=v<199.75?.25:-.25;
                _frameHorizontalPercent=h+dh;UpdateCamera();
                bool horizontalValid=ProjectZoomAnchor(anchor,out var horizontal);
                _frameHorizontalPercent=h;_frameVerticalPercent=v+dv;UpdateCamera();
                bool verticalValid=ProjectZoomAnchor(anchor,out var vertical);
                _frameVerticalPercent=v;
                if(!horizontalValid||!verticalValid){UpdateCamera();break;}
                var a=(horizontal-current)/dh;var b=(vertical-current)/dv;
                double determinant=a.X*b.Y-a.Y*b.X;
                if(Math.Abs(determinant)<1e-9){UpdateCamera();break;}
                _frameHorizontalPercent=Math.Clamp(h+(error.X*b.Y-error.Y*b.X)/determinant,-200,200);
                _frameVerticalPercent=Math.Clamp(v+(a.X*error.Y-a.Y*error.X)/determinant,-200,200);
                UpdateCamera();
                if(Math.Abs(_frameHorizontalPercent-h)+Math.Abs(_frameVerticalPercent-v)<1e-9)break;
            }
        }
        finally{_adjustingPointerZoom=false;UpdateCamera();}
        CameraInteracted?.Invoke(this);
        return true;
    }

    internal bool TryPointerZoomAnchor(Point pointer,out Point3D anchor)
    {
        anchor=new Point3D();
        double width=_viewport.ActualWidth,height=_viewport.ActualHeight;
        if(width<=1||height<=1||!double.IsFinite(pointer.X+pointer.Y)||
            pointer.X<0||pointer.Y<0||pointer.X>width||pointer.Y>height)return false;
        var forward=_camera.LookDirection;
        double depth=forward.Length;if(depth<1e-9)return false;forward.Normalize();
        var right=Vector3D.CrossProduct(forward,_camera.UpDirection);
        if(right.LengthSquared<1e-12)return false;right.Normalize();
        var up=Vector3D.CrossProduct(right,forward);up.Normalize();
        double tangent=Math.Tan(_camera.FieldOfView*Math.PI/360);
        var ray=forward+right*((2*pointer.X/width-1)*tangent)+
            up*((1-2*pointer.Y/height)*tangent*height/width);
        ray.Normalize();
        double closest=double.PositiveInfinity;
        // The retained WPF scene is also the DX11 source. Ray-test that graph
        // directly, including when its Viewport3D is hidden by the DX11 surface.
        foreach(Visual3D visual in _viewport.Children)
            VisualTreeHelper.HitTest(visual,null,result=>
            {
                if(result is RayMeshGeometry3DHitTestResult hit&&
                    hit.DistanceToRayOrigin>=_camera.NearPlaneDistance&&hit.DistanceToRayOrigin<closest)
                    closest=hit.DistanceToRayOrigin;
                return HitTestResultBehavior.Continue;
            },new RayHitTestParameters(_camera.Position,ray));
        if(double.IsFinite(closest))anchor=_camera.Position+ray*closest;
        else anchor=_camera.Position+ray*(depth/Vector3D.DotProduct(ray,forward));
        return true;
    }

    private bool ProjectZoomAnchor(Point3D anchor,out Point screen)
    {
        screen=new Point();double width=_viewport.ActualWidth,height=_viewport.ActualHeight;
        var forward=_camera.LookDirection;if(width<=1||height<=1||forward.LengthSquared<1e-12)return false;forward.Normalize();
        var right=Vector3D.CrossProduct(forward,_camera.UpDirection);right.Normalize();
        var up=Vector3D.CrossProduct(right,forward);up.Normalize();
        var offset=anchor-_camera.Position;double depth=Vector3D.DotProduct(offset,forward);
        if(depth<=_camera.NearPlaneDistance)return false;
        double scale=width/(2*depth*Math.Tan(_camera.FieldOfView*Math.PI/360));
        screen=new Point(width*.5+Vector3D.DotProduct(offset,right)*scale,
            height*.5-Vector3D.DotProduct(offset,up)*scale);
        return double.IsFinite(screen.X+screen.Y);
    }
}
