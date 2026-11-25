param(
    [Parameter(Mandatory=$true)][string]$StudioRoot,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference='Stop'
$StudioRoot=[IO.Path]::GetFullPath($StudioRoot).TrimEnd('\','/')
$extensions=@('.cs','.xaml','.csproj','.props','.targets','.hlsl','.vrmanifest')
$skip=@('bin','obj','packaging','.git','Releases','Diagnostics','Migration','Models','Native','Assets','FullBodyModel','test-data')
$files=New-Object 'System.Collections.Generic.List[string]'
function Add-SourceFiles([string]$folder) {
    foreach ($item in Get-ChildItem -LiteralPath $folder -Force) {
        if ($item.PSIsContainer) {
            if ($item.Name -notin $skip) { Add-SourceFiles $item.FullName }
        } elseif ($item.Name -notlike '*_wpftmp.csproj' -and
            ($item.Extension -in $extensions -or ($item.Extension -eq '.json' -and $item.Directory.Name -eq 'Input'))) {
            $files.Add($item.FullName)
        }
    }
}
foreach ($folder in @('ServoAnimator/ServoAnimator','Johnny5VREditor','Johnny5VR','Shared')) {
    Add-SourceFiles (Join-Path $StudioRoot $folder)
}
$files.Add($PSCommandPath)
$files.Sort([StringComparer]::Ordinal)
$hash=[Security.Cryptography.SHA256]::Create()
$utf8=New-Object Text.UTF8Encoding($false)
try {
    foreach ($file in $files) {
        $relative=$file.Substring($StudioRoot.Length+1).Replace('\','/')
        $content=[IO.File]::ReadAllText($file).Replace("`r`n","`n")
        $bytes=$utf8.GetBytes($relative + [char]0 + $content + [char]0)
        [void]$hash.TransformBlock($bytes,0,$bytes.Length,$bytes,0)
    }
    [void]$hash.TransformFinalBlock([byte[]]@(),0,0)
    $identity=([BitConverter]::ToString($hash.Hash)).Replace('-','').ToLowerInvariant()
} finally { $hash.Dispose() }
$wire=[IO.File]::ReadAllText((Join-Path $StudioRoot 'Shared/VR/SceneWire.cs'))
$protocol=[regex]::Match($wire,'public const int Version=(\d+)').Groups[1].Value
if (!$protocol) { throw 'Cannot read the VR scene protocol version.' }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath))
$text="<Project><PropertyGroup><StudioBuildId>$identity</StudioBuildId><StudioSceneProtocol>$protocol</StudioSceneProtocol></PropertyGroup></Project>"
if (!(Test-Path -LiteralPath $OutputPath) -or [IO.File]::ReadAllText($OutputPath) -ne $text) {
    [IO.File]::WriteAllText($OutputPath,$text)
}
