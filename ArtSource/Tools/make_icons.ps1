# Draws the skill, passive, item and power icons into Assets/Data/Images with System.Drawing (no Unity needed).
# The drawing code is in ArtSource/Tools/Icons: IconKit.cs (shapes and shading), the *Icons.cs files (one entry per icon).
#
#   powershell -File ArtSource/Tools/make_icons.ps1                    # every icon
#   powershell -File ArtSource/Tools/make_icons.ps1 -Only "Wolf|Wisp"  # by name or folder (regex)
#   powershell -File ArtSource/Tools/make_icons.ps1 -Only ItemIcons -Sheet sheet.png -Columns 8
#
# -Sheet skips drawing and writes a review sheet with each icon at 128 and 48 px.
# New PNGs get a sprite .meta; an existing .meta is left alone, so GUIDs stay stable when icons are redrawn.
param(
    [string]$Only = '.*',
    [string]$Sheet = '',
    [int]$Columns = 8
)
$ErrorActionPreference = 'Stop'

$project = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$images = Join-Path $project 'Assets\Data\Images'
$sources = (Get-ChildItem (Join-Path $PSScriptRoot 'Icons\*.cs')).FullName

Add-Type -Path $sources -ReferencedAssemblies 'System.Drawing'

if ($Sheet) { [IconRegistry]::Sheet($images, $Only, $Sheet, $Columns) }
else { [IconRegistry]::Run($images, $Only) }
