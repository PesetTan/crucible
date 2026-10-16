param([Parameter(Mandatory)][string]$OutputPath, [string]$ProcessOutputPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose(); $bitmap.Dispose() }
if ($ProcessOutputPath) {
    Get-CimInstance Win32_Process -Filter "Name = 'crucible-desktop.exe' OR Name = 'msedgewebview2.exe'" |
        Select-Object Name, ProcessId, ParentProcessId, CommandLine |
        ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8 $ProcessOutputPath
}
