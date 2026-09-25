param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$script=Join-Path $root 'src/Sterling.App/Tools/WindowsTools.ps1'
$hostExe=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
$name='Sterling v012 Test Queue';$port='Sterling-v012-Test-TCP'
$created=$false;$checks=@()
function RunTool([string]$operation,$request,[string]$label){
    $input=Join-Path $output ($label+'-request.json');$result=Join-Path $output ($label+'-result.json');$log=Join-Path $output ($label+'.log')
    $request | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $input -Encoding UTF8
    & $hostExe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $script -Operation $operation -RequestPath $input -ResultPath $result -LogPath $log
    if($LASTEXITCODE -ne 0){throw (Get-Content -LiteralPath $result -Raw)}
    return (Get-Content -LiteralPath $result -Raw | ConvertFrom-Json)
}
try {
    if(-not([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))){throw 'Printer integration test requires normal Windows elevation.'}
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $initial=RunTool 'PrintInventory' @{} 'initial'
    $console=RunTool 'PrintConsole' @{} 'console'
    $checks+=('Captured '+@($initial.Printers).Count+' real queues; default: '+(($initial.Printers | Where-Object Default).Name -join ', '))
    if(-not(Get-PrinterDriver -Name 'Microsoft Print To PDF' -ErrorAction SilentlyContinue)){throw 'Microsoft Print To PDF test driver is absent; no substitute driver was installed.'}
    RunTool 'PrinterTest' @{} 'create' | Out-Null; $created=$true
    $capture=Join-Path $output 'capture'
    $manifest=RunTool 'PrintBackup' @{Root=$capture;Names=@($name);UserSid=$sid} 'backup'
    if(@($manifest.Printers).Count -ne 1 -or $manifest.Printers[0].HostAddress -ne '192.0.2.123'){throw 'TCP/IP backup metadata mismatch'}
    $hashes=@{};foreach($f in Get-ChildItem -LiteralPath $capture -File -Recurse){$hashes[$f.FullName.Substring($capture.Length+1)]=(Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash}
    $hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $capture 'integrity.json') -Encoding UTF8
    $zip=Join-Path $output 'PrinterBackup.zip';Compress-Archive -Path (Join-Path $capture '*') -DestinationPath $zip
    $opened=Join-Path $output 'opened';Expand-Archive -LiteralPath $zip -DestinationPath $opened
    Remove-Printer -Name $name
    Remove-PrinterPort -Name $port
    $restored=RunTool 'PrintRestore' @{Root=$opened;Printers=@(@{Name=$name;Conflict='Skip existing'});UserSid=$sid;Default=$false;Preferences=$true} 'restore'
    if($restored.Results[0].Status -notin @('Restored','Partial')){throw ('Restore failed: '+($restored | ConvertTo-Json -Depth 10))}
    $queue=Get-Printer -Name $name;$tcp=Get-PrinterPort -Name $port
    if($queue.DriverName -ne 'Microsoft Print To PDF' -or $tcp.PrinterHostAddress -ne '192.0.2.123' -or $tcp.PortNumber -ne 9100){throw 'Restored queue/driver/TCP port verification failed'}
    $checks+='Created, backed up, ZIP-extracted and restored dedicated TCP/IP queue with Microsoft Print To PDF driver; no print job submitted.'
    $skip=RunTool 'PrintRestore' @{Root=$opened;Printers=@(@{Name=$name;Conflict='Skip existing'});UserSid=$sid;Default=$false;Preferences=$false} 'skip'
    if($skip.Results[0].Status -ne 'Skipped'){throw 'Existing queue was not skipped'}
    $checks+='Existing queue skip verified.'
    $checks+=('Settings/preferences result: '+$restored.Results[0].Detail)
    $health=RunTool 'Health' @{Step='CheckHealth';AlwaysRepair=$false} 'health'
    $checks+=('Real DISM CheckHealth exit '+$health.Results[0].Code+'; raw output retained.')
    @{Passed=$true;Checks=$checks;Console=$console;Restore=$restored;Health=$health;Limitations='No physical print output, third-party driver installation, shared server reconnect or change of the actual user default was tested.'} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $output 'verification.json') -Encoding UTF8
}catch{
    @{Passed=$false;Error=$_.Exception.ToString();Checks=$checks} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'verification.json') -Encoding UTF8
    throw
}finally{
    if($created){
        $queue=Get-Printer -Name $name -ErrorAction SilentlyContinue
        if($queue -and $queue.DriverName -eq 'Microsoft Print To PDF' -and $queue.PortName -eq $port){Remove-Printer -Name $name}
        if((Get-PrinterPort -Name $port -ErrorAction SilentlyContinue).PrinterHostAddress -eq '192.0.2.123' -and -not(Get-Printer | Where-Object PortName -eq $port)){Remove-PrinterPort -Name $port}
    }
}
