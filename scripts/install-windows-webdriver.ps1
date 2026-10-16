# CI helper: match EdgeDriver to the installed WebView2 runtime.
$ErrorActionPreference = 'Stop'
# Match the WebView2 runtime, which can differ from the installed Edge browser.
$runtimeKeys = @('HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')
$version = $runtimeKeys | ForEach-Object { if (Test-Path $_) { (Get-ItemProperty $_).pv } } | Where-Object { $_ -and $_ -ne '0.0.0.0' } | Select-Object -First 1
if (-not $version) { throw 'WebView2 must be available on the Windows test runner.' }
Invoke-WebRequest "https://msedgedriver.microsoft.com/$version/edgedriver_win64.zip" -OutFile "$env:RUNNER_TEMP/edgedriver.zip"
Expand-Archive "$env:RUNNER_TEMP/edgedriver.zip" "$env:RUNNER_TEMP/edgedriver" -Force
"$env:RUNNER_TEMP/edgedriver" | Out-File -FilePath $env:GITHUB_PATH -Encoding utf8 -Append
