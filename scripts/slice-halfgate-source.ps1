# slice-halfgate-source.ps1
# Slice halfgate-zone-iso-1024x512.png into 16 iso patches and write them to
# the Godot user:// override directory so they are picked up at runtime (F6).
#
# Usage:
#   .\scripts\slice-halfgate-source.ps1
#
# Input (hardcoded):
#   PKA Owner's Inbox\Media\E1\sources\halfgate-zone-iso-1024x512.png
#
# Output:
#   %APPDATA%\Godot\app_userdata\Wayfinders\wayfinders_visual_assets\e1\halfgate_face_b\
#   -> 16 x halfgate_face_b_NM.png  (N,M in 0..3)

$source = "C:\Users\dbo\Desktop\PKA\Owner's Inbox\Media\E1\sources\halfgate-zone-iso-1024x512.png"
$outDir = "$env:APPDATA\Godot\app_userdata\Wayfinders\wayfinders_visual_assets\e1\halfgate_face_b"
$script = "$PSScriptRoot\halfgate_face_b_slice_v6.py"

uv run python $script --input $source --output $outDir
