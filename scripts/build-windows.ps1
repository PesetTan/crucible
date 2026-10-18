[CmdletBinding()]
param([string]$Output = '', [switch]$SkipChecks)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Build the Windows installer on Windows (or use the Windows CI workflow).' }
$project = Split-Path $PSScriptRoot -Parent
if (-not $Output) { $Output = Join-Path $project 'dist/windows' }
$Output = [IO.Path]::GetFullPath($Output)
function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed ($LASTEXITCODE)." }
}
Push-Location $project
try {
    Invoke-Checked python @('scripts/export-public-source.py')
    Invoke-Checked python @('scripts/export-starter-catalog.py', '--verify')
    Invoke-Checked npm.cmd @('ci', '--prefix', 'src/Crucible.Ui', '--no-audit', '--no-fund')
    if (-not $SkipChecks) {
        Invoke-Checked npm.cmd @('--prefix', 'src/Crucible.Ui', 'run', 'check')
        Invoke-Checked npm.cmd @('--prefix', 'src/Crucible.Ui', 'test')
        Invoke-Checked dotnet @('run', '--project', 'tests/WindowsBetaChecks', '--', 'resources/starter')
        Invoke-Checked dotnet @('run', '--project', 'tests/ForgeWorkerChecks')
    }
    Invoke-Checked npm.cmd @('--prefix', 'src/Crucible.Ui', 'run', 'build')
    New-Item -ItemType Directory -Force $Output | Out-Null
    $resources = Join-Path $project 'src/Crucible.Ui/src-tauri/resources'
    # A Windows package must never contain the Mac checkout path or prior package contents.
    if (Test-Path $resources) { Remove-Item -Recurse -Force $resources }
    New-Item -ItemType Directory -Force $resources | Out-Null
    foreach ($hostRole in @(@('Crucible.Service','service','CrucibleBackend.exe'), @('Crucible.UiHost','ui','CrucibleUi.exe'))) {
        $hostOutput = Join-Path $resources $hostRole[1]
        Invoke-Checked dotnet @('publish', "src/$($hostRole[0])/$($hostRole[0]).csproj", '-c', 'Release', '-r', 'win-x64', '-o', $hostOutput, '--disable-build-servers', '-p:UseSharedCompilation=false', '-m:1', '--nologo')
        $check = & (Join-Path $hostOutput $hostRole[2]) --check-runtime
        if ($LASTEXITCODE -ne 0 -or ($check -join '') -notmatch 'native-aot') { throw 'Published host is not Native AOT.' }
        Get-ChildItem $hostOutput -Filter '*.pdb' -Recurse | Remove-Item
    }
    $assets = Join-Path $resources 'assets'
    New-Item -ItemType Directory -Force (Join-Path $assets 'starter'), (Join-Path $assets 'engine'), (Join-Path $assets 'docs') | Out-Null
    Copy-Item 'resources/starter/catalog.sqlite.gz', 'resources/starter/manifest.json' (Join-Path $assets 'starter')
    Copy-Item 'engine/ForgeBridge.java' (Join-Path $assets 'engine')
    (Get-FileHash 'engine/ForgeBridge.java' -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content (Join-Path $assets 'engine/bridge.sha256')
    Copy-Item 'THIRD_PARTY.md' $assets
    Copy-Item 'LICENSE' $assets
    Copy-Item 'docs/license-review.md' (Join-Path $assets 'docs')
    Invoke-Checked npm.cmd @('--prefix', 'src/Crucible.Ui', 'run', 'tauri', '--', 'build', '--target', 'x86_64-pc-windows-msvc', '--bundles', 'nsis', '--config', 'src-tauri/tauri.windows.conf.json', '--ci')
    $bundle = Join-Path $project 'src/Crucible.Ui/src-tauri/target/x86_64-pc-windows-msvc/release/bundle/nsis'
    $installers = @(Get-ChildItem $bundle -Filter '*-setup.exe')
    if ($installers.Count -ne 1) { throw 'Expected exactly one Windows NSIS installer.' }
    Copy-Item $installers[0].FullName $Output -Force
    Get-FileHash (Join-Path $Output $installers[0].Name) -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $($_.Path | Split-Path -Leaf)" } | Set-Content (Join-Path $Output 'SHA256SUMS.txt')
    Write-Output "Windows beta installer: $(Join-Path $Output $installers[0].Name)"
} finally { Pop-Location }
