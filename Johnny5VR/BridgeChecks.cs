using System.Numerics;
using Johnny5.VrBridge;

namespace Johnny5VR;

internal static class BridgeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var mesh=new VrMesh(5,false,[0,0,0,1,0,0,0,1,0],[0,0,1,0,0,1,0,0,1],[0,0,1,0,0,1],[0,1,2]);
        var matrix=Matrix4x4.CreateRotationZ(.3f)*Matrix4x4.CreateTranslation(2,3,4);
        var frame=new VrFrame(123,"Editing","robot.urdf",-.4f,1.8f,-.4f,
            [new(9,5,7,matrix,true)],[mesh],
            [new(7,new(.2f,.3f,.4f,.5f),new(.1f),new(.7f),45,11)],
            [new(11,[1,2,3,4])],[new(Vector4.One,Vector3.UnitY,false)]);
        var copy=SceneWire.Decode(SceneWire.Encode(frame,new HashSet<long>(),new HashSet<long>()));
        check(copy.Sequence==123&&copy.Nodes[0].World==matrix&&copy.Nodes[0].Robot,"Live editor protocol preserves camera-independent node transforms.");
        check(copy.Meshes[0].Indices.SequenceEqual(mesh.Indices)&&copy.Meshes[0].Normals.SequenceEqual(mesh.Normals)&&copy.Meshes[0].UV.SequenceEqual(mesh.UV),"Live editor protocol preserves geometry, smooth normals and texture coordinates.");
        check(copy.Materials[0]==frame.Materials[0]&&copy.Textures[0].Png.SequenceEqual(frame.Textures[0].Png)&&copy.Lights[0]==frame.Lights[0],"Live editor protocol preserves alpha, specular lighting and texture content.");
        var delta=SceneWire.Decode(SceneWire.Encode(frame,new HashSet<long>{5},new HashSet<long>{11}));
        check(delta.Meshes.Length==0&&delta.Textures.Length==0&&delta.Nodes.Length==1&&delta.Materials.Length==1,"Live editor pose updates do not resend cached geometry or textures.");
        bool invalid=false;
        try{SceneWire.Decode(SceneWire.Encode(frame with{Meshes=[mesh with{Indices=[0,1,99]}]},new HashSet<long>(),new HashSet<long>()));}catch(InvalidDataException){invalid=true;}
        check(invalid,"Live editor protocol rejects out-of-range triangle indices.");
        var sender=new VrSceneDeltaWriter();var receiver=new VrSceneAccumulator();
        var first=sender.Prepare(frame,0);var initial=receiver.Apply(SceneWire.Decode(SceneWire.Encode(first,new HashSet<long>(),new HashSet<long>())));
        var unchanged=sender.Prepare(frame,receiver.Sequence);
        check(unchanged.BaseSequence==initial.Sequence&&unchanged.Nodes.Length==0&&unchanged.Materials.Length==0,"Unchanged VR scenes send no node or material descriptions.");
        receiver.Apply(SceneWire.Decode(SceneWire.Encode(unchanged,new HashSet<long>{5},new HashSet<long>{11})));
        var movedFrame=frame with{Nodes=[frame.Nodes[0] with{World=Matrix4x4.CreateTranslation(4,5,6)}],Materials=[frame.Materials[0] with{Diffuse=Vector4.One}]};
        var moved=sender.Prepare(movedFrame,receiver.Sequence);var assembled=receiver.Apply(SceneWire.Decode(SceneWire.Encode(moved,new HashSet<long>{5},new HashSet<long>{11})));
        var camera=new VrCaptureCamera(new(2,3,4),new(1,0,0));
        var cameraSender=new VrSceneDeltaWriter();var cameraReceiver=new VrSceneAccumulator();
        cameraReceiver.Apply(cameraSender.Prepare(frame,0));
        var cameraDelta=cameraSender.Prepare(frame with{CaptureCamera=camera},cameraReceiver.Sequence);
        var cameraFrame=cameraReceiver.Apply(SceneWire.Decode(SceneWire.Encode(cameraDelta,new HashSet<long>(),new HashSet<long>())));
        check(cameraDelta.Nodes.Length==0&&cameraFrame.CaptureCamera==camera,"Capture camera travels with scene deltas independently of geometry.");
        check(moved.Nodes.Length==1&&moved.Materials.Length==1&&assembled.Nodes[0].World==movedFrame.Nodes[0].World&&assembled.Materials[0].Diffuse==Vector4.One,"Delta transfer preserves changed transforms and same-ID material edits.");
        // The renderer may skip the preceding ready frame; the background accumulator may not skip a packet.
        var skippedReady=sender.Prepare(movedFrame with{Nodes=[movedFrame.Nodes[0],new(10,5,7,Matrix4x4.Identity,false)]},receiver.Sequence);receiver.Apply(skippedReady);
        var removed=sender.Prepare(movedFrame with{Nodes=[new(10,5,7,Matrix4x4.Identity,false)]},receiver.Sequence);var latest=receiver.Apply(removed);
        check(removed.RemovedNodes.SequenceEqual(new long[]{9})&&latest.Nodes.Length==1&&latest.Nodes[0].Id==10,"Removal remains correct when an intermediate complete frame is not displayed.");
        bool baselineRejected=false;try{new VrSceneAccumulator().Apply(removed);}catch(InvalidDataException){baselineRejected=true;}
        check(baselineRejected,"A delta without its exact baseline is rejected.");
        var recovery=sender.Prepare(frame,0);var recovered=new VrSceneAccumulator().Apply(recovery);
        check(recovery.BaseSequence==0&&recovered.Nodes.Length==1,"A missing client baseline forces a complete recovery snapshot.");
        var treads=new VrTreads(.01,-.025,.013,[new(9,true,[5,6,7,8])]);
        var treadPacket=SceneWire.Decode(SceneWire.Encode(frame with{Treads=treads},new HashSet<long>{5},new HashSet<long>{11}));
        check(treadPacket.Treads is {} t&&t.PhaseStep==.01&&t.LeftTravel==-.025&&t.RightTravel==.013&&t.Layers.Length==1&&t.Layers[0].Node==9&&t.Layers[0].Left&&t.Layers[0].Frames.SequenceEqual(new long[]{5,6,7,8}),"Tread belt frames and travel survive the scene protocol.");
        var treadSender=new VrSceneDeltaWriter();var treadReceiver=new VrSceneAccumulator();
        treadReceiver.Apply(treadSender.Prepare(frame with{Treads=treads},0));var treadDelta=treadReceiver.Apply(treadSender.Prepare(frame with{Treads=treads with{LeftTravel=-.03}},treadReceiver.Sequence));
        check(treadDelta.Treads?.LeftTravel==-.03,"Tread travel is current in every delta packet.");
        check(VrTreads.FrameIndex(0,.01,4)==0&&VrTreads.FrameIndex(-.01,.01,4)==1&&VrTreads.FrameIndex(.01,.01,4)==3&&VrTreads.FrameIndex(-.045,.01,4)==0,"Tread frame choice matches the editor (forward travel counts frames backwards, wrapping).");
        check(EditorConnection.TargetCycle(false,1,0)==EditorConnection.SceneCycleMs,"Outside Follow, scenes stay at 30 Hz.");
        check(EditorConnection.TargetCycle(true,2,1)==EditorConnection.FastestFollowCycleMs,"While following with a lightly loaded editor, scenes come at up to 75 Hz.");
        check(Math.Abs(EditorConnection.TargetCycle(true,10,1)-25)<1e-9,"Scene work keeps at most 40% of the editor's UI thread.");
        check(Math.Abs(EditorConnection.TargetCycle(true,2,5)-20)<1e-9,"Applying scenes keeps at most 25% of the player's render thread.");
        check(EditorConnection.TargetCycle(true,30,1)==EditorConnection.SceneCycleMs,"A busy editor never gets scenes slower than the normal 30 Hz.");
        var replacement=sender.Prepare(frame with{Model="another.urdf"},recovery.Sequence);
        check(replacement.BaseSequence==0,"Changing robot scenes forces a complete snapshot.");
        var head=Matrix4x4.CreateRotationY(.4f)*Matrix4x4.CreateTranslation(0,1.6f,0);
        var origin=Matrix4x4.CreateRotationY(.8f)*Matrix4x4.CreateTranslation(3,0,2);
        var world=EditorVrPanel.Place(head,origin);Matrix4x4.Invert(origin,out var inverse);
        var physical=world*inverse;var roundTrip=physical*origin;
        check(Vector3.Distance(roundTrip.Translation,world.Translation)<1e-5f,"VR editor workspace stays fixed in the virtual room during locomotion.");
        check(EditorVrPanel.Coordinates(new(-1,.5f,0),2,1)==Vector2.Zero&&EditorVrPanel.Coordinates(new(1,-.5f,0),2,1)==Vector2.One,"VR editor pointer maps top-left and bottom-right correctly.");
        var desktop=new EditorDesktopFrame([],100,80,new(-1200,200,1000,800),[]);
        check(EditorDesktop.Map(desktop,0,0)==new Point(-1200,200)&&EditorDesktop.Map(desktop,1,1)==new Point(-201,999),"VR editor pointer supports negative desktop monitor coordinates and exact window edges.");
    }
}
