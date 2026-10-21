$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
[xml]$project=Get-Content -LiteralPath Assembly-CSharp.csproj -Raw
$engine=$project.Project.ItemGroup.Reference.HintPath | Where-Object {$_ -like '*Editor\Data\Managed\UnityEngine\UnityEngine.dll'} | Select-Object -First 1
$modules=Split-Path $engine -Parent
$editorData=Split-Path (Split-Path $modules -Parent) -Parent
$output=Join-Path (Get-Location) Temp/HiveMindChecks
New-Item -ItemType Directory -Force -Path $output | Out-Null
$arguments=@('/nologo','/target:exe','/nostdlib+',('/out:"'+$output+'/AcousticBusChecks.exe"'),('/reference:"'+$editorData+'/NetStandard/ref/2.1.0/netstandard.dll"'),('/reference:"'+$editorData+'/NetStandard/compat/2.1.0/shims/netfx/mscorlib.dll"'),('/reference:"'+$modules+'/UnityEngine.CoreModule.dll"'),('/reference:"'+$modules+'/UnityEngine.SharedInternalsModule.dll"'),'Assets/Scripts/hive_mind/AcousticBus.cs','Tests/AcousticBusChecks.cs')
$response=Join-Path $output checks.rsp
[System.IO.File]::WriteAllLines($response,$arguments)
& (Join-Path $editorData NetCoreRuntime/dotnet.exe) (Join-Path $editorData DotNetSdkRoslyn/csc.dll) "@$response"
if($LASTEXITCODE -ne 0){throw 'Acoustic bus check compilation failed'}
$previousMonoPath=$env:MONO_PATH
try {
    $env:MONO_PATH=$modules
    & (Join-Path $editorData MonoBleedingEdge/bin/mono.exe) (Join-Path $output AcousticBusChecks.exe)
    if($LASTEXITCODE -ne 0){throw 'Acoustic bus checks failed'}
} finally { $env:MONO_PATH=$previousMonoPath }
