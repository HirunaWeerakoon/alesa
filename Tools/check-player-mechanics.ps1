$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
[xml]$project=Get-Content -LiteralPath Assembly-CSharp.csproj -Raw
$references=@($project.Project.ItemGroup.Reference.HintPath | Where-Object {$_ -and (Test-Path -LiteralPath $_)})
$engine=$references | Where-Object {$_ -like '*Editor\Data\Managed\UnityEngine\UnityEngine.dll'} | Select-Object -First 1
if(-not $engine){throw 'Regenerate Unity project files first: Assembly-CSharp.csproj must reference the installed editor.'}
$editorData=Split-Path (Split-Path (Split-Path $engine -Parent) -Parent) -Parent
$runtime=Join-Path $editorData NetCoreRuntime/dotnet.exe
$compiler=Join-Path $editorData DotNetSdkRoslyn/csc.dll
$output=Join-Path (Get-Location) Temp/PlayerMechanicsChecks
New-Item -ItemType Directory -Force -Path $output | Out-Null
$arguments=@('/nologo','/target:library','/langversion:9','/nostdlib+',"/out:$output/PlayerMechanics.dll")
$arguments+=@($references | ForEach-Object {'/reference:"'+$_+'"'})
$arguments+=@(Get-ChildItem -LiteralPath Assets/Scripts -Filter '*.cs' | ForEach-Object {'"'+$_.FullName+'"'})
$arguments+='Assets/inputs/PlayerInputs.cs'
$compileResponse=Join-Path $output compile.rsp
[System.IO.File]::WriteAllLines($compileResponse,$arguments)
& $runtime $compiler "@$compileResponse"
if($LASTEXITCODE -ne 0){throw 'Unity script compilation failed'}
$testArguments=@('/nologo','/target:exe','/nostdlib+',"/out:$output/AmmoChecks.exe",("/reference:$editorData/NetStandard/ref/2.1.0/netstandard.dll"),("/reference:$editorData/NetStandard/compat/2.1.0/shims/netfx/mscorlib.dll"),("/reference:$editorData/NetStandard/compat/2.1.0/shims/netfx/System.dll"),'Assets/Scripts/RevolverAmmo.cs','Tests/RevolverAmmoChecks.cs')
$testResponse=Join-Path $output ammo.rsp
[System.IO.File]::WriteAllLines($testResponse,$testArguments)
Add-Content -LiteralPath $testResponse -Value 'Assets/Scripts/FiringStopSoundGate.cs'
& $runtime $compiler "@$testResponse"
if($LASTEXITCODE -ne 0){throw 'Ammo check compilation failed'}
& (Join-Path $editorData MonoBleedingEdge/bin/mono.exe) (Join-Path $output AmmoChecks.exe)
if($LASTEXITCODE -ne 0){throw 'Ammo checks failed'}
node Tools/check-player-mechanics.cjs
if($LASTEXITCODE -ne 0){throw 'Scene/input checks failed'}
