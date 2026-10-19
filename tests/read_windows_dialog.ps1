param([Parameter(Mandatory)][long]$WindowHandle)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$WindowHandle)
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Subtree, [System.Windows.Automation.Condition]::TrueCondition)
$names = @($elements | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
ConvertTo-Json -InputObject @{ processId = $root.Current.ProcessId; names = $names } -Compress
