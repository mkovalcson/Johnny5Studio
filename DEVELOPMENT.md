# Develop J5 Studio

Use Windows x64, the .NET 10 SDK, and PowerShell. Open `Johnny5Studio.slnx` or build from the repo root:

```powershell
dotnet build Johnny5Studio.slnx -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Initialize-Config.ps1
```

ConfigTemplate contains the ready-to-use full-body and head URDFs, scenery and runtime meshes.
It becomes Config at setup or in Desktop deployment. Open Projects/TipToe-Demo.json and
choose the included TipToe Demo scene if needed. Live Config is ignored.
Use a separate copy of Config for each simultaneous editor session.

- Desktop: `ServoAnimator/ServoAnimator/bin/Release/net10.0-windows/AnimationEditorPlayer.exe`
- VR Player: `dotnet run --project Johnny5VR/Johnny5VR.csproj -c Release -- --config Config`
- For VR editing, publish the fork to the location the Player looks for, then open VR Editor from the Player:

```powershell
$version = (Get-Content ServoAnimator/Build/PROJECT_VERSION.json -Raw | ConvertFrom-Json).version
dotnet publish Johnny5VREditor/ServoAnimator.csproj -c Release -r win-x64 --self-contained true -o "Johnny5VR/packaging/editor-$version" "-p:StudioReleaseVersion=$version"
```

The Desktop deploy script uses local Config if it exists and ConfigTemplate otherwise. Run
`Create-Deploy.ps1 -WhatIf` before using it. The optional integrations have their own setup:
[Face Tracking](ServoAnimator/README.md), [Editor MCP](Johnny5-Editor-MCP/README.md),
[Stream Deck](StreamDeckXL/README.md). Keep client registrations and virtual environments local.

## Release version

The Animation Editor owns the release stamp in `ServoAnimator/Build/PROJECT_VERSION.json`.
Solution builds and standalone VR builds reuse the recorded version. To allocate the
next version for a release, first build the Desktop project directly with
`dotnet build ServoAnimator/ServoAnimator/ServoAnimator.csproj -c Release`, or publish
Desktop with an explicit version using its release script. Then publish both VR apps
and build or pack the Stream Deck plug-in using the same checkout. The VR packaging
script checks both published versions before making the ZIP. The MCP server reports
the checkout's release with `python Johnny5-Editor-MCP/johnny5_editor_mcp.py --version`
using its installed Python environment. Desktop includes the same stamp alongside
its Face Tracking files. Protocol versions advance independently.

## Checks

Create the isolated Config using [the fixture instructions](TestFixtures/README.md).
Run these from the repository root after the Release build:

```powershell
dotnet run --project Shared/Pose.Checks/J5Studio.Pose.Checks.csproj -c Release --no-build -- Shared/Pose.Checks/editor-baseline.json
dotnet run --project Johnny5VR/Johnny5VR.csproj -c Release --no-build -- --follow-controls-check
```

Run the VR Editor pose checks before the full VR Player check. The editor produces the
pose catalog and bounds used by the Player checks; these generated files are ignored.

```powershell
$config = (Resolve-Path TestFixtures/Work/Config).Path
$report = Join-Path (Get-Location) 'Johnny5VR/Diagnostics/vr-pose-check.txt'
$editor = 'Johnny5VREditor/bin/Release/net10.0-windows/AnimationEditorPlayer.exe'
$process = Start-Process -FilePath $editor -ArgumentList ('--vr-pose-check "{0}" "{1}"' -f $config,$report) -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "VR pose checks failed. See $report." }
dotnet run --project Johnny5VR/Johnny5VR.csproj -c Release --no-build -- --config TestFixtures/Work/Config --check
```

See [VR checks](Johnny5VR/README.md) and [check notes](Documentation/README.md) for
editor bridge, pose and additional checks. Shared check projects include JSON baselines.
The historical Configuration baseline was captured against different settings; see
[the known baseline difference](Documentation/Insert-Audio-Library-Copy.md).
Use SteamVR and the target headset/controllers to validate input, performance, and display behavior.
Offline checks do not establish headset or physical hardware compatibility.

The source folders are the three apps, Shared libraries, native/runtime assets and optional integrations.
Help is built from the editors' Help folders, Shared/UserHelp and selected Documentation pages.
Before publishing, review source, settings, samples and asset redistribution rights. Do not include
personal projects, calibration, recordings or local MCP configuration.

## Initial staging validation

The Release solution built from this checkout. The shared pose checks (5,724 assertions),
focused Follow checks (57), VR Editor pose checks, and VR Player checks (4,208) passed with
this checkout's fixtures. Headset and physical hardware were not tested. This checkout has
no GitHub remote configured.