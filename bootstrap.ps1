$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$payload=[Console]::ReadLine()|ConvertFrom-Json
if($payload.id -notmatch '^[a-f0-9]{32}$'){throw 'Invalid session identifier'}
$base=[IO.Path]::GetTempPath()
$fixed=@([IO.DriveInfo]::GetDrives()|Where-Object {$_.DriveType -eq [IO.DriveType]::Fixed -and $_.IsReady}|ForEach-Object {$_.Name})
foreach($drive in @('E:\','D:\')){
    if($fixed -contains $drive){$base=$drive;break}
}
$root=Join-Path (Join-Path $base 'SSH-Screen-Temp') $payload.id
$taskNames=@()
$scheduler=$null
$folder=$null
$state=[hashtable]::Synchronized(@{At=[DateTime]::UtcNow;Stop=$false;Beats=0})
$reader=$null
$seenReady=$false
function WriteUtf8([string]$path,[string]$text){[IO.File]::WriteAllText($path,$text,(New-Object Text.UTF8Encoding($true)))}
function StartInteractive([string]$name,[string]$script){
    $d=$scheduler.NewTask(0)
    $d.Principal.UserId=[Security.Principal.WindowsIdentity]::GetCurrent().Name
    $d.Principal.LogonType=3
    $d.Principal.RunLevel=1
    $d.Settings.Enabled=$true
    $d.Settings.AllowDemandStart=$true
    $d.Settings.ExecutionTimeLimit='PT0S'
    $d.Settings.DisallowStartIfOnBatteries=$false
    $d.Settings.StopIfGoingOnBatteries=$false
    $d.Settings.MultipleInstances=2
    $a=$d.Actions.Create(0)
    $a.Path=$env:WINDIR+'\System32\WindowsPowerShell\v1.0\powershell.exe'
    $a.Arguments='-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$script+'" -Root "'+$root+'"'
    $a.WorkingDirectory=$root
    $t=$folder.RegisterTaskDefinition($name,$d,6,$d.Principal.UserId,$null,3,$null)
    $null=$t.Run($null)
}
try{
    New-Item -ItemType Directory -Path ($root+'\temp') -Force|Out-Null
    $null=& icacls.exe $root /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F'
    if($LASTEXITCODE -ne 0){throw 'Temporary directory ACL failed'}
    $config=@{token=$payload.token;port=47191;ownerPid=$PID;ownerTicks=(Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks}
    WriteUtf8 ($root+'\agent.json') ($config|ConvertTo-Json -Compress)
    WriteUtf8 ($root+'\Common.cs') $payload.common
    WriteUtf8 ($root+'\Agent.cs') $payload.agent
    $agentScript=@'
param([string]$Root)
$ErrorActionPreference='Stop'
$env:TEMP=$Root+'\temp'
$env:TMP=$env:TEMP
try{
    $cp=New-Object CodeDom.Compiler.CompilerParameters
    $cp.CompilerOptions='/unsafe /optimize+';$cp.GenerateInMemory=$true
    $cp.ReferencedAssemblies.AddRange([string[]]@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.Security.dll'))
    Add-Type -Path @(($Root+'\Common.cs'),($Root+'\Agent.cs')) -CompilerParameters $cp
    [SshScreen.Runtime]::Run($Root)
}catch{[IO.File]::WriteAllText(($Root+'\error.txt'),$_.Exception.Message)}finally{
    try{
        $cfg=[IO.File]::ReadAllText(($Root+'\agent.json'))|ConvertFrom-Json
        $owner=Get-Process -Id $cfg.ownerPid -ErrorAction SilentlyContinue
        if(!$owner -or $owner.StartTime.ToUniversalTime().Ticks -ne $cfg.ownerTicks){
            [IO.File]::WriteAllText(($Root+'\test-stop'),'stop')
            Start-Sleep -Milliseconds 400
            $id=Split-Path $Root -Leaf
            $others=@(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'"|Where-Object{$_.ProcessId -ne $PID -and $_.CommandLine -like ('*'+$id+'*')})
            foreach($p in $others){Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue}
            $s=New-Object -ComObject Schedule.Service;$s.Connect();$f=$s.GetFolder('\')
            foreach($n in @(('SshScreenTemp-'+$id),('SshScreenTest-'+$id))){try{$f.DeleteTask($n,0)}catch{}}
            Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }catch{}
}
'@
    WriteUtf8 ($root+'\run.ps1') $agentScript
    $scheduler=New-Object -ComObject Schedule.Service
    $scheduler.Connect()
    $folder=$scheduler.GetFolder('\')
    $agentTask='SshScreenTemp-'+$payload.id
    $taskNames+=@($agentTask)
    StartInteractive $agentTask ($root+'\run.ps1')
    if($payload.test){
        if($payload.mouseParkProbe){WriteUtf8 ($root+'\mouse-park-probe') 'probe'}
        WriteUtf8 ($root+'\TestWindow.cs') $payload.testWindow
        $testScript=@'
param([string]$Root)
$ErrorActionPreference='Stop'
$env:TEMP=$Root+'\temp';$env:TMP=$env:TEMP
try{
    $cp=New-Object CodeDom.Compiler.CompilerParameters
    $cp.CompilerOptions='/unsafe /optimize+';$cp.GenerateInMemory=$true
    $cp.ReferencedAssemblies.AddRange([string[]]@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.Security.dll'))
    Add-Type -Path @(($Root+'\Common.cs'),($Root+'\TestWindow.cs')) -CompilerParameters $cp
    [SshScreen.TestWindow]::Main([string[]]@($Root))
}catch{[IO.File]::WriteAllText(($Root+'\test-error.json'),($_.Exception.Message|ConvertTo-Json))}
'@
        WriteUtf8 ($root+'\test.ps1') $testScript
        $testTask='SshScreenTest-'+$payload.id
        $taskNames+=@($testTask)
        StartInteractive $testTask ($root+'\test.ps1')
    }
    $deadline=[DateTime]::UtcNow.AddSeconds(25)
    while(!(Test-Path ($root+'\ready.json')) -or ($payload.test -and !(Test-Path ($root+'\test-ready.json')))){
        if(Test-Path ($root+'\error.txt')){throw ([IO.File]::ReadAllText(($root+'\error.txt')))}
        if(Test-Path ($root+'\test-error.json')){throw ([IO.File]::ReadAllText(($root+'\test-error.json')))}
        if([DateTime]::UtcNow -gt $deadline){throw 'Interactive process startup timed out'}
        Start-Sleep -Milliseconds 80
    }
    $ready=[IO.File]::ReadAllText(($root+'\ready.json'))|ConvertFrom-Json
    $reply=[ordered]@{ready=$true;id=$payload.id;pid=$ready.pid;session=$ready.session;port=$ready.port;process=$ready.process;build=$ready.build;agentSha=$ready.agent_sha;commonSha=$ready.common_sha}
    if($payload.test){$reply.test=([IO.File]::ReadAllText(($root+'\test-ready.json'))|ConvertFrom-Json)}
    [Console]::WriteLine(($reply|ConvertTo-Json -Depth 5 -Compress));[Console]::Out.Flush();$seenReady=$true
    while(([DateTime]::UtcNow-[IO.File]::GetLastWriteTimeUtc(($root+'\alive'))).TotalSeconds -lt 4){
        if(Test-Path ($root+'\stopped.json')){break}
        Start-Sleep -Milliseconds 100
    }
}catch{
    [Console]::WriteLine((@{ready=$false;error=$_.Exception.Message}|ConvertTo-Json -Compress));[Console]::Out.Flush()
}finally{
    if(Test-Path $root){
        WriteUtf8 ($root+'\stop') 'stop'
        WriteUtf8 ($root+'\test-stop') 'stop'
    }
    $until=[DateTime]::UtcNow.AddSeconds(3)
    do{
        $owned=@(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue|Where-Object{$_.ProcessId -ne $PID -and $_.CommandLine -like ('*'+$payload.id+'*')})
        if($owned.Count -eq 0){break}
        Start-Sleep -Milliseconds 100
    }while([DateTime]::UtcNow -lt $until)
    foreach($p in $owned){Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue}
    $proof=$null;$stopped=$null
    if(Test-Path ($root+'\test-proof.json')){$proof=[IO.File]::ReadAllText(($root+'\test-proof.json'))|ConvertFrom-Json}
    if(Test-Path ($root+'\stopped.json')){$stopped=[IO.File]::ReadAllText(($root+'\stopped.json'))|ConvertFrom-Json}
    foreach($name in $taskNames){try{$folder.DeleteTask($name,0)}catch{}}
    if(Test-Path $root){Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue}
    $remaining=@(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue|Where-Object{$_.ProcessId -ne $PID -and $_.CommandLine -like ('*'+$payload.id+'*')})
    $cleanup=[ordered]@{cleanup=$true;id=$payload.id;processes=$remaining.Count;directoryRemoved=(!(Test-Path $root));stopped=$stopped;lifetimeTransport='SSH-forwarded control ping'}
    if($proof){$cleanup.testProof=$proof}
    [Console]::WriteLine(($cleanup|ConvertTo-Json -Depth 6 -Compress));[Console]::Out.Flush()
}
