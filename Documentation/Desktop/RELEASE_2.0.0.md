# J5 Animation Editor & Player 2.0.0

This release includes continuous physical timeline spline targets matching the
URDF preview, cursor-pose starts, Break Spline holds, complementary neck mixing,
and visual-only Hide Group. It also reorganizes Help, improves its navigation
and rendering, fixes status-popup contrast, updates application naming and About,
and names the detached preview URDF Model.

Robot defaults and backdrop paths support Config-relative references, independent
of the process working directory. Release staging includes the full-body robot,
head/backdrop models and their meshes. The Sylogist scene and image are omitted;
the deployment retains the other libraries. The repository Config is a separate
minimal copy with no Library, Projects, scenes, layout, recent files or recovery
snapshot. Existing live configuration/library files are never replaced or deleted.

To reproduce the explicit version:

```powershell
dotnet build ServoAnimator/ServoAnimator.csproj -c Release -p:EditorReleaseVersion=2.0.0
```

Normal later builds resume the existing daily/patch increment rule. The packaging
script is tools/Create-Release.ps1; run it with -Version for a new release staging
directory. Validate the staged files with tools/Verify-Release.py before creating
the archive. Face-tracking Python setup and hardware drivers remain destination
machine prerequisites. The Python virtual environment is not distributed.

See TEST-RESULTS-2026-10-08.md for the release validation results.
