# Build against your installed ATT + MelonLoader. Does not install or modify game files.
param([string]$GamePath = $env:ATT_GAME_DIR)
$ErrorActionPreference = 'Stop'
if (-not $GamePath) { throw 'Pass -GamePath pointing to your A Township Tale game folder.' }
$managed = Join-Path $GamePath 'A Township Tale_Data/Managed'
$loader = Join-Path $GamePath 'MelonLoader/net35'
$compiler = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
if (-not (Test-Path -LiteralPath $managed)) { throw "Game assemblies not found: $managed" }
if (-not (Test-Path -LiteralPath (Join-Path $loader 'MelonLoader.dll'))) { throw 'Patch the game with MelonLoader first.' }
$out = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $out | Out-Null
$references = @(Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object { '/r:' + $_.FullName })
$references += '/r:' + (Join-Path $loader 'MelonLoader.dll')
$harmony = Join-Path $loader '0Harmony.dll'
if (-not (Test-Path -LiteralPath $harmony)) { throw 'MelonLoader Harmony assembly not found.' }
$references += '/r:' + $harmony
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$arguments = @('/target:library', '/nostdlib+', '/noconfig', '/warn:4', ('/out:' + (Join-Path $out 'ATTSmoothCamera.dll'))) + $references + $sources
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Camera mod compilation failed.' }
Get-Item -LiteralPath (Join-Path $out 'ATTSmoothCamera.dll') | Select-Object FullName, Length
