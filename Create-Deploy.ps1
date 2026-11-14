[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter()]
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',

    [Parameter()]
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $RuntimeIdentifier = 'win-x64',

    [Parameter()]
    [switch] $IncludeRecoverySnapshot,

    [Parameter()]
    [switch] $KeepPreviousDeployment
)
& (Join-Path $PSScriptRoot 'ServoAnimator/Create-Deploy.ps1') @PSBoundParameters
