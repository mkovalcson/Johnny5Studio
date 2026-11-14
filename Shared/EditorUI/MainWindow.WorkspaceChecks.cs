using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ServoAnimator;
public partial class MainWindow
{
    private static bool _workspaceCheckActive;
    internal static void RunWorkspaceChecks(string report)
    {
        _workspaceCheckActive = true;
        var app = new App(); app.InitializeComponent();
        var results = new List<string>();
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); results.Add("PASS: " + message); File.WriteAllLines(report, results); }
        var window = new MainWindow();
        try
        {
            // No Show / Loaded: no device polling, Config loading or recovery dialogs.
            new WindowInteropHelper(window).EnsureHandle();
            window._doc = new AnimationDocument
            {
                CommandGroupNumbers = new() { ["left"] = 1, ["right"] = 2 },
                CommandGroupNames = new() { ["left"] = "Arm take", ["right"] = "Other take" },
                CommandGroupTimelines = new() { "left", "right" },
                Commands = new() {
                    new() { Servo = ServoNames.NeckTurn, OffsetSeconds = 1, NumericValue = 20, GroupId = "left" },
                    new() { Servo = ServoNames.NeckTurn, OffsetSeconds = 3, NumericValue = 30, GroupId = "left" },
                    new() { Servo = ServoNames.Whip_Antenna_Rotate, OffsetSeconds = 1, NumericValue = -10, GroupId = "right" } }
            };
            window.RefreshMarkers();
            Check(window._namedGroupTracks.Count == 2, "Each requested group has an independent timeline strip.");
            window.SelectNamedGroup("left");
            Check(window.SelectedGroupCommands().Count == 2 && window.SelectedGroupCommands().All(c=>c.GroupId=="left"), "Selecting a group excludes other commands at the same timestamps.");
            var metadata = window.CaptureGroupMetadata();
            window._doc.CommandGroupNames["left"] = "Changed"; window._doc.CommandGroupTimelines.Clear();
            window.RestoreGroupMetadata(metadata);
            Check(window._doc.CommandGroupNames["left"] == "Arm take" && window._doc.CommandGroupTimelines.Count==2, "Undo metadata snapshots restore names and timeline visibility independently.");
            var clone = window._doc.Clone(); clone.CommandGroupNames["left"]="Clone"; clone.CommandGroupTimelines.Remove("right");
            Check(window._doc.CommandGroupNames["left"]=="Arm take" && window._doc.CommandGroupTimelines.Contains("right"), "Document clones isolate group settings.");
            string temporary=Path.Combine(Path.GetTempPath(),"j5-groups-"+Guid.NewGuid().ToString("N")+".json");
            try { window._doc.Save(temporary); var loaded=AnimationDocument.Load(temporary);
                Check(loaded.CommandGroupNames["left"]=="Arm take" && loaded.CommandGroupTimelines.SetEquals(new[]{"left","right"}), "Group names and timeline visibility survive saving and reopening."); }
            finally { if(File.Exists(temporary))File.Delete(temporary); }
            window._doc.HiddenCommandGroups.Add("left"); window.RefreshGroupVisibility();
            Check(window._doc.Commands.Count==3 && window.VisibleCommands.Count()==1 && window._namedGroupTracks.Count==2, "Display hiding preserves playback commands and independently visible group rows.");
            window._doc.HiddenCommandGroups.Clear();
            window.SetGroupTimeline("right",false);
            Check(window._namedGroupTracks.Count==1 && window._doc.Commands.Count==3, "Hiding a timeline preserves its commands.");
            window.UndoSteps(1);
            Check(window._namedGroupTracks.Count==2, "Undo restores a hidden group timeline.");
            window.MoveNamedGroup("left",.5);
            Check(window._doc.Commands.Where(c=>c.GroupId=="left").Select(c=>c.OffsetSeconds).SequenceEqual(new[]{1.5,3.5}) && window._doc.Commands.Single(c=>c.GroupId=="right").OffsetSeconds==1, "Group movement leaves overlapping groups untouched.");
            window.UndoSteps(1);
            Check(window._doc.Commands.Where(c=>c.GroupId=="left").Select(c=>c.OffsetSeconds).SequenceEqual(new[]{1d,3d}), "Group movement is a single undoable operation.");
            window.ChangeGroupMetadata("Rename group", () => window._doc.CommandGroupNames["left"]="Renamed arm");
            Check(window._doc.CommandGroupNames["left"]=="Renamed arm", "Renaming updates the active document.");
            window.UndoSteps(1);
            Check(window._doc.CommandGroupNames["left"]=="Arm take", "Undo restores the previous group name.");
            window.Redo_Executed(null, null);
            Check(window._doc.CommandGroupNames["left"]=="Renamed arm", "Redo restores the new group name.");
            window._cursorTime=10; window.DuplicateNamedGroup("left");
            var copy=window._doc.Commands.Where(c=>c.GroupId!="left"&&c.GroupId!="right").ToArray();
            Check(copy.Length==2 && copy.Select(c=>c.OffsetSeconds).SequenceEqual(new[]{10d,12d}) && window._doc.CommandGroupNames[copy[0].GroupId]=="Renamed arm copy", "Duplicating creates an independently named group at the cursor with preserved spacing.");
            var strip=window._namedGroupTracks[0]; strip.Measure(new Size(800,50)); strip.Arrange(new Rect(0,0,800,50));
            var bitmap=new RenderTargetBitmap(800,50,96,96,PixelFormats.Pbgra32); bitmap.Render(strip);
            Check(bitmap.PixelWidth==800, "Group timeline strips render their command marks, span and cursor.");
            window._controllerProfiles[ControllerKind.Xbox]=ControllerProfile.Defaults(ControllerKind.Xbox);
            window._controllerConnections[ControllerKind.Xbox].Connected=true;
            window._controllerConnections[ControllerKind.Xbox].SetEnabled(true);
            var originalParent=window.CommandsAtPointContent.Parent;
            window.FloatControlView_Click(null,new RoutedEventArgs());
            Check(window._controlFloat!=null && !ReferenceEquals(window.CommandsAtPointContent.Parent, originalParent), "Control View floats with its existing editing controls.");
            Check(window.ActiveControllerMapping.Visibility==Visibility.Visible, "A connected controller diagram stays docked when only the Control View is floated.");
            window._controllerConnections[ControllerKind.Xbox].Connected=false; window.RefreshActiveControllerMapping();
            window._urdfUndocked=true; window.UpdateVacatedWorkspace(); window.FitEditorPanels();
            Check(window.TopEditorRow.Height.Value==0 && window.TopEditorRow.MinHeight==0, "Floating Control and URDF views releases the entire upper editor row.");
            window._urdfUndocked=false; window._controlFloat.Close();
            Check(ReferenceEquals(window.CommandsAtPointContent.Parent,originalParent) && window.TopEditorRow.Height.Value>0, "Closing the floating Control View docks it and restores usable height.");
            window.ToggleFloatingController(ControllerKind.Xbox); window.ToggleFloatingController(ControllerKind.Steam);
            Check(window._floatingControllers.Count==2, "Xbox and Steam mapping views float independently.");
            foreach(var entry in window._floatingControllers.Values.ToArray())entry.Window.Close();
            Check(window._floatingControllers.Count==0, "Closing controller windows returns them to docked availability.");
            var deckParent=window.StreamDeckControlPanel.Parent;
            window.FloatStreamDeckView_Click(null,new RoutedEventArgs());
            Check(window._streamDeckFloat!=null && !ReferenceEquals(deckParent,window.StreamDeckControlPanel.Parent), "Existing Stream Deck controls float without cloning device state.");
            window._streamDeckFloat.Close();
            Check(ReferenceEquals(deckParent,window.StreamDeckControlPanel.Parent), "Stream Deck controls return to their original location.");
            window.ShowGroupManager();
            Check(window._groupManager.Content is DockPanel, "Named group manager opens with editable names and actions.");
            window._groupManager.Close();
            results.Add($"PASS: {results.Count} focused workspace checks completed; no headset or hardware tests run."); File.WriteAllLines(report,results);
        }
        catch(Exception ex) { results.Add(ex.ToString()); File.WriteAllLines(report,results); Environment.ExitCode=1; }
        finally { window.CloseWorkspaceViews(); }
        app.Shutdown();
    }
}
