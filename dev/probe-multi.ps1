# Runs outside the Claude sandbox via a scheduled task: invokes the "Lichtbak" verb on three
# files at once, like a right-click on an Explorer selection, then copies the log here.
$dir = Join-Path $PSScriptRoot 'samples'
$log = Join-Path $env:APPDATA 'Lichtbak\lichtbak.log'
if (Test-Path $log) { Remove-Item $log }
$shell = New-Object -ComObject Shell.Application
$items = $shell.Namespace($dir).Items()
$items.Filter(0x40, 'photo*.jpg;logo.png')
"items: $($items.Count)" | Out-File (Join-Path $PSScriptRoot 'probe-multi.txt') -Encoding utf8
$items.InvokeVerbEx('Lichtbak')
Start-Sleep -Seconds 6
& "$env:LOCALAPPDATA\Programs\Lichtbak\Lichtbak.exe" --quit
Start-Sleep -Seconds 1
Get-Content $log | Out-File (Join-Path $PSScriptRoot 'probe-multi.txt') -Append -Encoding utf8
