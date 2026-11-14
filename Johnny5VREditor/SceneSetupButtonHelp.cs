using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ServoAnimator;

internal static class SceneSetupButtonHelp
{
    internal static void Attach(Button button, Window owner)
    {
        string explanation = button.Content?.ToString() switch
        {
            "Apply" => "Keep the current robot, background, placement and camera after closing, without creating or overwriting a saved scene. Meeting can inherit this setup.",
            "Change Robot Model…" => "Choose and load the robot's URDF file, retaining the background. Save Scene stores the complete combination.",
            "Choose Robot Model…" => "Choose the robot's URDF file. Press Load Robot afterward to apply it. This does not select scenery.",
            "Load Robot" => "Load the selected robot URDF while retaining the background. Restore this model's saved framing when available. Save Scene to store the full combination.",
            "Load Background" => "Apply the selected scenery preset while keeping the current robot model, robot placement and framing.",
            "Save Background Only…" => "Save a named scenery preset. This does not save the robot model, robot placement or camera framing. Use Save Scene in the bottom action bar for the complete setup.",
            "Manage Backgrounds…" => "Create, edit and organize scenery presets by name, category and description, or load a saved background.",
            "Load Scene…" => "Load the selected robot model, background, placement and camera settings together. This replaces the current scene setup.",
            "Save Scene" => "Save the current robot, background, placement and camera to the scene shown under Editing. Asks for a name if this scene has not been saved yet. Also applies the setup.",
            "Save Scene As…" => "Save the complete current setup as a new named scene, with a category and description. Keeps the existing scene file unchanged.",
            "Save Scene and Assign to Sequence" => "Save the edited scene and assign it to the active sequence. Save the sequence afterward to retain its scene assignment.",
            "Record Position" => "Record numeric placement and camera commands at the cursor, including 3D background pan/zoom. Smooth movement enables spline interpolation. Recording again at the same time updates the commands. Save the sequence afterward.",
            "Close" => "Close Scene Setup. If preview changes remain, choose Apply and Close, Discard Preview, or Keep Editing. Applied settings remain in effect.",
            "Choose background URDF…" => "Select a 3D scenery URDF, separate from the robot model. Adjust placement in Robot Position and framing in Camera.",
            "Configure 3D stage…" => "Edit the 3D backdrop and stage settings.",
            "Chromakey Green" => "Preview a solid chromakey-green background. Save Scene to retain it with the robot and camera, or Save Background Only for a scenery preset.",
            "Choose background image…" => "Choose an image from any folder. A copy is stored in Config/Backdrops and used from there. Selecting the same unchanged file reuses its copy. Select Image or MovingImage settings for its presentation, then Apply or save a scene.",
            _ => null
        };
        if (explanation == null) return;
        button.ToolTip = new ToolTip
        {
            Content = new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap, MaxWidth = 310, Foreground = Brushes.WhiteSmoke },
            Background = new SolidColorBrush(Color.FromRgb(28, 39, 54)),
            BorderBrush = Brushes.SlateGray, Padding = new Thickness(12),
            Placement = PlacementMode.Right, PlacementTarget = owner, HorizontalOffset = 24,
            IsHitTestVisible = false
        };
        ToolTipService.SetInitialShowDelay(button, 650);
        ToolTipService.SetShowDuration(button, 20000);
    }
}
