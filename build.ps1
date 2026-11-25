param([Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference='Stop'
$env:TEMP=$Root+'\temp';$env:TMP=$env:TEMP
New-Item -ItemType Directory -Path $env:TEMP -Force|Out-Null
$csc=$env:WINDIR+'\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs=@('/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll','/reference:System.IO.Compression.dll','/reference:System.Security.dll')
$common=@(($Root+'\src\Common.cs'),($Root+'\src\Session.cs'))
& $csc /nologo /target:winexe /platform:x64 /unsafe+ /optimize+ /codepage:65001 @refs ('/out:'+($Root+'\SSH-Screen-Control.exe')) @common ($Root+'\src\RouteModel.cs') ($Root+'\src\RouteStudio.cs') ($Root+'\src\PlayerVision.cs') ($Root+'\src\Controller.cs')
if($LASTEXITCODE -ne 0){throw 'Controller build failed'}
if(Test-Path ($Root+'\src\Validate.cs')){
    & $csc /nologo /target:exe /platform:x64 /unsafe+ /optimize+ /codepage:65001 @refs ('/out:'+($Root+'\SSH-Screen-Validate.exe')) @common ($Root+'\src\Validate.cs')
    if($LASTEXITCODE -ne 0){throw 'Validation build failed'}
}
Get-ChildItem ($Root+'\SSH-Screen-*.exe')|ForEach-Object{[ordered]@{file=$_.Name;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash;bytes=$_.Length}}|ConvertTo-Json
