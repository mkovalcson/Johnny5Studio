# J5 Studio VR Editor

This is an independent source copy of the desktop Animation Editor, based on version 2.10.14. The original source and installed desktop executable remain under `../ServoAnimator/ServoAnimator`.

Launch this copy through Johnny5VR's **Open J5 Studio VR Editor** button. The viewer starts the bundled executable with an explicit Config folder and a random, same-user named-pipe session. The editor sends its evaluated scene geometry, joint transforms, materials, lighting and media surfaces to the VR viewer. Headset tracking and locomotion remain controlled by the viewer.

Only this VR copy hides Face Tracking, Meeting mode and the 2D URDF viewport. Those desktop features remain in the original editor. Editor camera framing does not move the headset camera. Authoring and saving in the VR editor writes to the Config folder selected in the viewer; use the bundled Config to keep VR work separate from desktop documents.

The copied project deliberately does not import the desktop version-stamping target. Building or publishing this project must not update the original editor's source, version metadata or executable.

Build a self-contained copy:

```powershell
dotnet publish ServoAnimator.csproj -c Release -r win-x64 --self-contained true -o ../Johnny5VR/packaging/editor-0.6.1
```

Physical headset interaction still requires testing on the target SteamVR PC. The desktop scene, live connection, media decoder and WPF-rendered workspace image streaming have automated diagnostics in the viewer project.

