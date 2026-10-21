import {execFileSync} from 'node:child_process';
import {existsSync} from 'node:fs';
import {resolve,join} from 'node:path';

if(process.platform!=='win32')throw Error('WINDOWS_REQUIRED');
const args=process.argv.slice(2);
if(args.length!==0&&!(args.length===2&&args[0]==='--previous-root'))throw Error('USAGE: [--previous-root old-installation-directory]');
const root=resolve(import.meta.dirname,'..');
const previousRoot=args.length?resolve(args[1]):'';
if(!existsSync(join(root,'connection/background-tunnel.exe')))throw Error('RUN_BUILD_WINDOWS_FIRST');
// The root is passed as environment data; no paths or secrets are interpolated as PowerShell code.
const script=String.raw`
$ErrorActionPreference='Stop'
$rootPath=$env:GOOGLE_TASKS_STARTUP_ROOT
$launcher=Join-Path $rootPath 'connection/background-tunnel.exe'
$previousLauncher=if($env:GOOGLE_TASKS_PREVIOUS_ROOT){Join-Path $env:GOOGLE_TASKS_PREVIOUS_ROOT 'connection/background-tunnel.exe'}else{$launcher}
$taskName='Google Tasks MCP'
$owner=[Security.Principal.WindowsIdentity]::GetCurrent()
$existing=Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
$existingOwner=$null
if($existing) {
  try {
    $identity=$existing.Principal.UserId
    if($identity -like 'S-1-*') { $existingOwner=$identity }
    else { $account=New-Object Security.Principal.NTAccount($identity); $existingOwner=$account.Translate([Security.Principal.SecurityIdentifier]).Value }
  } catch { throw 'EXISTING_STARTUP_TASK_OWNER_UNRESOLVED' }
}
if($existing -and (@($existing.Actions).Count -ne 1 -or $existing.Actions[0].Execute -notin @($launcher,$previousLauncher) -or $existingOwner -ne $owner.User.Value)) { throw 'EXISTING_STARTUP_TASK_DIFFERS' }
$action=New-ScheduledTaskAction -Execute $launcher -WorkingDirectory $rootPath
$trigger=New-ScheduledTaskTrigger -AtLogOn -User $owner.User.Value
$trigger.Delay='PT15S'
$principal=New-ScheduledTaskPrincipal -UserId $owner.User.Value -LogonType Interactive -RunLevel Limited
$settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
$null=Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Private Google Tasks tunnel at desktop sign-in, using its approved local runtime key.' -Force
Start-ScheduledTask -TaskName $taskName
Write-Output 'STARTUP_TASK_INSTALLED_AND_STARTED_VERIFY_CLOUD_READ_SEPARATELY'
`;
const powershell=join(process.env.SystemRoot??'C:\\Windows','System32/WindowsPowerShell/v1.0/powershell.exe');
execFileSync(powershell,['-NoProfile','-NonInteractive','-Command',script],{
  windowsHide:true,stdio:'inherit',env:{...process.env,GOOGLE_TASKS_STARTUP_ROOT:root,GOOGLE_TASKS_PREVIOUS_ROOT:previousRoot}
});
