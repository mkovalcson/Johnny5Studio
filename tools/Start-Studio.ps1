[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('Desktop','VRPlayer','VREditor')]
    [string]$Application,
    [ValidateSet('Release','Debug')]
    [string]$Configuration='Release'
)
$ErrorActionPreference='Stop'
$studioRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $studioRoot
try {
    & dotnet build (Join-Path $studioRoot 'Johnny5Studio.slnx') -c $Configuration -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'J5 Studio could not build. See the errors above.' }
    if ($Application -eq 'Desktop') {
        $executable=Join-Path $studioRoot "ServoAnimator/ServoAnimator/bin/$Configuration/net10.0-windows/AnimationEditorPlayer.exe"
    } else {
        $executable=Join-Path $studioRoot "Johnny5VR/bin/$Configuration/net10.0-windows/Johnny5VR.exe"
    }
    $launch=@{FilePath=$executable;WorkingDirectory=$studioRoot}
    if ($Application -eq 'VREditor') { $launch.ArgumentList='--open-editor' }
    Start-Process @launch | Out-Null
} finally {
    Pop-Location
}
