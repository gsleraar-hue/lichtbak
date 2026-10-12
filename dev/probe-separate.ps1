# Runs outside the Claude sandbox via a scheduled task: turns "each picture in its own window"
# on, opens three files at once, logs how many windows appeared, then restores the setting.
$dir = Join-Path $PSScriptRoot 'samples'
$out = Join-Path $PSScriptRoot 'probe-separate.txt'
$ini = Join-Path $env:APPDATA 'Lichtbak\settings.ini'
$log = Join-Path $env:APPDATA 'Lichtbak\lichtbak.log'
$backup = if (Test-Path $ini) { Get-Content $ini } else { @() }
($backup | Where-Object { $_ -notlike 'separatewindows=*' }) + 'separatewindows=1' | Set-Content $ini -Encoding ascii
if (Test-Path $log) { Remove-Item $log }
$shell = New-Object -ComObject Shell.Application
$items = $shell.Namespace($dir).Items()
$items.Filter(0x40, 'photo*.jpg;logo.png')
$items.InvokeVerbEx('Lichtbak')
Start-Sleep -Seconds 5
& "$env:LOCALAPPDATA\Programs\Lichtbak\Lichtbak.exe" --quit
Start-Sleep -Seconds 2
if ($backup.Count) { $backup | Set-Content $ini -Encoding ascii } else { Remove-Item $ini }
Get-Content $log | Out-File $out -Encoding utf8
