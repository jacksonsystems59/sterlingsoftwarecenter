param([Parameter(Mandatory)][ValidateSet('PrintInventory','PrintBackup','PrintRestore','PrintConsole','EnablePrintConsole','Health','InstallChocolatey','PrinterTest')][string]$Operation,[Parameter(Mandatory)][string]$RequestPath,[Parameter(Mandatory)][string]$ResultPath,[Parameter(Mandatory)][string]$LogPath)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$request=Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json
function Log([string]$text) { Add-Content -LiteralPath $LogPath -Value $text -Encoding UTF8 }
function Native([string]$exe,[string[]]$arguments) {
    Log ($exe+' '+($arguments -join ' '))
    $lines = & $exe @arguments 2>&1 | ForEach-Object { $s=$_.ToString().Replace([string][char]0,''); Log $s; $s }
    return @{Code=$LASTEXITCODE;Output=($lines -join "`n")}
}
function SafePath([string]$root,[string]$relative) {
    if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or ($relative.Split([char[]]'\/') -contains '..')) { throw 'Unsafe bundle path' }
    $full=[IO.Path]::GetFullPath((Join-Path $root $relative)); if(-not $full.StartsWith([IO.Path]::GetFullPath($root).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Bundle path escapes root'}; return $full
}
function Snapshot {
    $drivers=@(Get-PrinterDriver); $ports=@(Get-PrinterPort); $win=@(Get-CimInstance Win32_Printer)
    $items=@(foreach($p in Get-Printer){
        Log ('Reading printer configuration: '+$p.Name)
        $d=$drivers | Where-Object {$_.Name -eq $p.DriverName -and $_.PrinterEnvironment -eq 'Windows x64'} | Select-Object -First 1
        $port=$ports | Where-Object Name -eq $p.PortName | Select-Object -First 1
        $w=$win | Where-Object Name -eq $p.Name | Select-Object -First 1
        $kind=if($port.PrinterHostAddress){'TCP/IP'}elseif($port.PortMonitor -eq 'Local Monitor'){'Local'}else{'Unsupported monitor'}
        $status=if($kind -eq 'Unsupported monitor'){'Manual: WSD/IPP/vendor monitor; rediscover using Windows/vendor software.'}else{'Can recreate queue if driver is available.'}
        $config=''; try{$config=(Get-PrintConfiguration -PrinterName $p.Name).PrintTicketXML}catch{$status+=' Settings unavailable: '+$_.Exception.Message}
        $dv=DriverVersion $d
        [pscustomobject]@{Name=$p.Name;DriverName=$p.DriverName;DriverVersion=$dv;Architecture=$d.PrinterEnvironment;InfPath=$d.InfPath;PortName=$p.PortName;PortKind=$kind;HostAddress=$port.PrinterHostAddress;PortNumber=$port.PortNumber;Protocol=$port.Protocol;LprQueueName=$port.LprQueueName;SNMPEnabled=$port.SNMPEnabled;SNMPCommunity=$port.SNMPCommunity;SNMPDevIndex=$port.SNMPDevIndex;Connection=$(if($p.Type -eq 'Connection'){$p.Name}else{''});Shared=$p.Shared;ShareName=$p.ShareName;Default=[bool]$w.Default;PrintTicket=$config;Status=$status;PreferenceFile='';DriverFolder=''}
    })
    return @{SchemaVersion=1;Architecture=$env:PROCESSOR_ARCHITECTURE;WindowsVersion=[Environment]::OSVersion.Version.ToString();User=[Security.Principal.WindowsIdentity]::GetCurrent().Name;UserSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;CapturedAt=[DateTimeOffset]::UtcNow.ToString('o');Printers=$items}
}
function DriverVersion($driver) { if(-not $driver.DriverVersion){return ''};$v=[uint64]$driver.DriverVersion;return ('{0}.{1}.{2}.{3}' -f (($v -shr 48) -band 65535),(($v -shr 32) -band 65535),(($v -shr 16) -band 65535),($v -band 65535)) }
function VerifyBundle([string]$root) {
    $hashes=Get-Content -LiteralPath (Join-Path $root 'integrity.json') -Raw | ConvertFrom-Json
    foreach($entry in $hashes.PSObject.Properties){$f=SafePath $root $entry.Name; if((Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash -ne $entry.Value){throw ('Changed or missing bundle file: '+$entry.Name)}}
    foreach($file in Get-ChildItem -LiteralPath $root -File -Recurse){$relative=$file.FullName.Substring($root.TrimEnd('\').Length+1);if($relative -ne 'integrity.json' -and $hashes.PSObject.Properties.Name -notcontains $relative){throw ('Unlisted bundle file: '+$relative)}}
}
try {
    $answer=@{}
    switch($Operation){
        'PrintInventory' {$answer=Snapshot}
        'PrintConsole' {
            $console=Join-Path $env:WINDIR 'System32\printmanagement.msc'
            $valid=Test-Path -LiteralPath $console
            if($valid){try{[xml]$xml=Get-Content -LiteralPath $console -Raw; $valid=$null -ne $xml.DocumentElement}catch{$valid=$false}}
            $answer=@{Present=$valid;Path=$console;Status=$(if($valid){'Console file is valid. Open to verify the MMC snap-in on this PC.'}else{'Console absent. Enable checks the capabilities offered by this Windows installation.'})}
        }
        'EnablePrintConsole' {
            $cap=Get-WindowsCapability -Online | Where-Object Name -like 'Print.Management.Console*' | Select-Object -First 1
            if(-not $cap){throw 'This Windows image does not offer Print Management Console. Use Windows Settings / vendor tools.'}
            Log ('Capability: '+$cap.Name+' state '+$cap.State)
            if($cap.State -ne 'Installed'){$change=Add-WindowsCapability -Online -Name $cap.Name; Log ($change | Out-String)}
            $verified=Get-WindowsCapability -Online -Name $cap.Name
            if($verified.State -ne 'Installed' -or -not(Test-Path "$env:WINDIR\System32\printmanagement.msc")){throw 'Capability installation could not be verified. Check Windows Update/policy and servicing logs.'}
            $answer=@{Status='Installed and verified';RestartNeeded=[bool]$change.RestartNeeded}
        }
        'PrintBackup' {
            $root=[IO.Path]::GetFullPath($request.Root); New-Item -ItemType Directory -Path $root -Force | Out-Null
            $snap=Snapshot; $snap.Printers=@($snap.Printers | Where-Object {$request.Names -contains $_.Name})
            if($snap.Printers.Count -eq 0){throw 'No selected printers remain available.'}
            $driverRecords=@(); try{$driverRecords=@(Get-WindowsDriver -Online -All)}catch{Log ('Driver-store enumeration unavailable: '+$_.Exception.Message)}
            $index=0
            foreach($p in $snap.Printers){
                Log ('Capturing '+$p.Name)
                if($snap.UserSid -eq $request.UserSid){
                    try{$relative='preferences/'+$index+'.dat';$dest=SafePath $root $relative; New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
                        $r=Native "$env:WINDIR\System32\rundll32.exe" @('printui.dll,PrintUIEntry','/Ss','/q','/n',$p.Name,'/a',$dest,'u')
                        if($r.Code -eq 0 -and (Test-Path -LiteralPath $dest) -and (Get-Item -LiteralPath $dest).Length -gt 0){$p.PreferenceFile=$relative}else{$p.Status+=' Per-user preferences not exported.'}
                    }catch{$p.Status+=' Preferences unavailable: '+$_.Exception.Message}
                }else{$p.Status+=' Elevated identity differs; per-user preferences/default must be captured as the intended user.'; $p.Default=$false}
                $record=$driverRecords | Where-Object {$_.ClassName -eq 'Printer' -and $_.OriginalFileName -eq $p.InfPath} | Select-Object -First 1
                if($record -and -not $record.Inbox){
                    $relative='drivers/'+$index; $dest=SafePath $root $relative; New-Item -ItemType Directory -Path $dest -Force | Out-Null
                    $r=Native "$env:WINDIR\System32\pnputil.exe" @('/export-driver',$record.Driver,$dest)
                    if($r.Code -eq 0 -and @(Get-ChildItem -LiteralPath $dest -Filter '*.inf' -Recurse).Count -gt 0){$p.DriverFolder=$relative}else{$p.Status+=' Driver export failed; obtain vendor driver.'}
                }else{$p.Status+=' Driver not exported: inbox or unmatched driver; must exist on target Windows.'}
                $index++
            }
            $snap | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $root 'manifest.json') -Encoding UTF8
            $snap.Printers | Format-List * | Out-String -Width 240 | Set-Content -LiteralPath (Join-Path $root 'Inventory.txt') -Encoding UTF8
            $answer=$snap
        }
        'PrintRestore' {
            $root=[IO.Path]::GetFullPath($request.Root); VerifyBundle $root
            $bundle=Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
            if($bundle.SchemaVersion -ne 1 -or $bundle.Architecture -ne $env:PROCESSOR_ARCHITECTURE -or ([version]$bundle.WindowsVersion).Major -ne [Environment]::OSVersion.Version.Major){throw 'Unsupported printer bundle/Windows architecture/version.'}
            $sameUser=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value -eq $request.UserSid
            $results=@(foreach($choice in $request.Printers){
                $p=$bundle.Printers | Where-Object Name -eq $choice.Name | Select-Object -First 1
                $state='Failed'; $detail=''; $changed=$false
                try{
                    if(-not $p){throw 'Printer absent from the validated manifest.'}
                    $existing=Get-Printer -Name $p.Name -ErrorAction SilentlyContinue
                    if($existing -and $choice.Conflict -ne 'Replace queue'){$state='Skipped';$detail='Existing queue left unchanged.'}
                    else {
                        if($p.Connection -and -not $sameUser){throw 'Shared connection must be restored in the intended user session.'}
                        if(-not $p.Connection -and $p.PortKind -notin @('TCP/IP','Local')){throw 'Unsupported WSD/IPP/vendor monitor. Rediscover this printer or use vendor software.'}
                        $driver=Get-PrinterDriver -Name $p.DriverName -ErrorAction SilentlyContinue | Where-Object PrinterEnvironment -eq $p.Architecture | Select-Object -First 1
                        if($driver -and $p.DriverVersion){$version=DriverVersion $driver; if($version -ne $p.DriverVersion){throw 'Existing driver version differs. No driver overwrite performed; reconcile driver versions in Print Management.'}}
                        if(-not $driver -and -not $p.Connection){
                            if($p.DriverFolder){$folder=SafePath $root $p.DriverFolder; foreach($inf in Get-ChildItem -LiteralPath $folder -Filter '*.inf' -Recurse){$r=Native "$env:WINDIR\System32\pnputil.exe" @('/add-driver',$inf.FullName); if($r.Code -notin @(0,3010)){throw 'Driver staging failed.'}}}
                            Add-PrinterDriver -Name $p.DriverName
                            if(-not(Get-PrinterDriver -Name $p.DriverName -ErrorAction SilentlyContinue)){throw 'Driver install not verified.'}
                        }
                        if(-not $p.Connection){
                            $port=Get-PrinterPort -Name $p.PortName -ErrorAction SilentlyContinue
                            if($port -and $p.PortKind -eq 'TCP/IP' -and ($port.PrinterHostAddress -ne $p.HostAddress -or $port.PortNumber -ne $p.PortNumber -or $port.Protocol -ne $p.Protocol)){throw 'Port conflict. Existing port retained; resolve address/protocol in Print Management.'}
                            if(-not $port){
                                if($p.PortKind -eq 'TCP/IP'){
                                    if($p.Protocol -eq 2){Add-PrinterPort -Name $p.PortName -LprHostAddress $p.HostAddress -LprQueueName $p.LprQueueName}
                                    else{$portArgs=@{Name=$p.PortName;PrinterHostAddress=$p.HostAddress;PortNumber=$p.PortNumber};if($p.SNMPEnabled){$portArgs.SNMP=$(if($p.SNMPDevIndex){$p.SNMPDevIndex}else{1});$portArgs.SNMPCommunity=$p.SNMPCommunity};Add-PrinterPort @portArgs}
                                }else{if($p.PortName -notmatch '^(PORTPROMPT:|FILE:|nul:|COM[0-9]+:|LPT[0-9]+:)$'){throw 'Custom local port requires manual review.'};Add-PrinterPort -Name $p.PortName}
                            }
                        }
                        if($existing){Remove-Printer -Name $p.Name; $changed=$true}
                        if($p.Connection){Add-Printer -ConnectionName $p.Connection}else{Add-Printer -Name $p.Name -DriverName $p.DriverName -PortName $p.PortName}
                        $changed=$true
                        $verify=Get-Printer -Name $p.Name
                        if(-not $verify -or (-not $p.Connection -and ($verify.DriverName -ne $p.DriverName -or $verify.PortName -ne $p.PortName))){throw 'Queue recreation could not be verified.'}
                        $state='Restored';$detail='Queue/driver/port verified. '
                        if($p.PrintTicket){try{Set-PrintConfiguration -PrinterName $p.Name -PrintTicketXml $p.PrintTicket;$detail+='Print ticket applied; vendor UI verification recommended. '}catch{$state='Partial';$detail+='Settings failed: '+$_.Exception.Message+' '}}
                        if($p.Shared){$state='Partial';$detail+='Server sharing/ACLs not recreated automatically; configure sharing in Print Management. '}
                        if($request.Preferences -and $p.PreferenceFile){
                            if($sameUser){$r=Native "$env:WINDIR\System32\rundll32.exe" @('printui.dll,PrintUIEntry','/Sr','/q','/n',$p.Name,'/a',(SafePath $root $p.PreferenceFile),'u','i');$state='Partial';$detail+='Per-user preference restore requested (exit '+$r.Code+'); verify vendor-specific preferences manually. '}
                            else{$state='Partial';$detail+='Preferences skipped: elevated identity differs from selected user. '}
                        }
                        elseif($request.Preferences){$state='Partial';$detail+='Per-user preferences were not captured; configure them manually. '}
                        if($request.Default -and $p.Default){
                            if($sameUser){(New-Object -ComObject WScript.Network).SetDefaultPrinter($p.Name);if(-not(Get-CimInstance Win32_Printer | Where-Object {$_.Name -eq $p.Name -and $_.Default})){throw 'Default printer not verified.'};$detail+='User default verified. '}
                            else{$state='Partial';$detail+='Default skipped: run as the selected Windows user. '}
                        }
                    }
                }catch{$state=$(if($changed){'Partial'}else{'Failed'});$detail=$_.Exception.Message;Log ($_.InvocationInfo.PositionMessage)}
                Log ($choice.Name+': '+$state+' — '+$detail)
                [pscustomobject]@{Name=$choice.Name;Status=$state;Detail=$detail}
            })
            $answer=@{Results=$results}
        }
        'Health' {
            $allowed=@('CheckHealth','ScanHealth','RestoreHealth','SFC','Full');if($request.Step -notin $allowed){throw 'Unsupported health operation'}
            $steps=if($request.Step -eq 'Full'){@('CheckHealth','ScanHealth','RestoreHealth','SFC')}else{@($request.Step)}
            $results=@();$repairable=$false;$unrepairable=$false
            foreach($step in $steps){
                if($step -eq 'RestoreHealth' -and $request.Step -eq 'Full' -and -not $repairable -and -not $request.AlwaysRepair){$results+=@{Step=$step;Code=0;Output='Skipped: corruption was not reported and repair was not explicitly selected.';Skipped=$true};continue}
                if($step -eq 'SFC' -and $unrepairable){$results+=@{Step=$step;Code=0;Output='Skipped: component-store repair needs further action.';Skipped=$true};continue}
                Log ('CURRENT STEP: '+$step)
                $r=if($step -eq 'SFC'){Native "$env:WINDIR\System32\sfc.exe" @('/scannow')}else{Native "$env:WINDIR\System32\dism.exe" @('/English','/Online','/Cleanup-Image',('/'+$step),'/NoRestart')}
                $results+=@{Step=$step;Code=$r.Code;Output=$r.Output;Skipped=$false}
                if($r.Output -match 'component store is repairable'){$repairable=$true}
                if($r.Output -match 'cannot be repaired' -or ($step -eq 'RestoreHealth' -and $r.Code -notin @(0,3010))){$unrepairable=$true}
            }
            $answer=@{Results=$results}
        }
        'InstallChocolatey' {
            if(-not $request.Package -or -not(Test-Path -LiteralPath $request.Package)){throw 'Choose an administrator-approved Chocolatey nupkg first.'}
            if((Get-FileHash -LiteralPath $request.Package -Algorithm SHA256).Hash -ne $request.Sha256){throw 'Approved Chocolatey package hash mismatch'}
            $dest=Join-Path (Split-Path $ResultPath) 'choco';Add-Type -AssemblyName System.IO.Compression.FileSystem
            [IO.Compression.ZipFile]::ExtractToDirectory($request.Package,$dest)
            $install=Join-Path $dest 'tools\chocolateyInstall.ps1'; if(-not(Test-Path -LiteralPath $install)){throw 'Not a Chocolatey installer package'}
            $env:chocolateyDownloadUrl=$request.Package
            & $install *>&1 | ForEach-Object {Log $_.ToString()}
            $exe=Join-Path $env:ProgramData 'chocolatey\bin\choco.exe';if(-not(Test-Path -LiteralPath $exe)){throw 'Chocolatey installation did not produce choco.exe'}
            $r=Native $exe @('--version');if($r.Code -ne 0){throw 'Chocolatey verification failed'}
            $disabled=Native $exe @('source','disable','--name=chocolatey');if($disabled.Code -ne 0){throw 'Chocolatey installed, but default Community source could not be disabled. Configure it before use.'}
            $answer=@{Status=('Chocolatey verified: '+$r.Output+'. Default Community source disabled. Configure an authorised internal source in Sterling.')}
        }
        'PrinterTest' {
            $name='Sterling v012 Test Queue';$port='Sterling-v012-Test-TCP'
            if(Get-Printer -Name $name -ErrorAction SilentlyContinue){throw 'Test queue already exists; refusing to overwrite.'}
            if(Get-PrinterPort -Name $port -ErrorAction SilentlyContinue){throw 'Test port already exists; refusing to overwrite.'}
            Add-PrinterPort -Name $port -PrinterHostAddress '192.0.2.123' -PortNumber 9100
            Add-Printer -Name $name -DriverName 'Microsoft Print To PDF' -PortName $port
            $answer=@{Name=$name;Port=$port;Status='Created isolated test queue; no print job submitted.'}
        }
    }
    $answer | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
    exit 0
}catch{Log $_.Exception.ToString();@{Error=$_.Exception.Message} | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8;exit 1}



