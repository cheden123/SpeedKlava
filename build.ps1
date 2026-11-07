$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
New-Item -ItemType Directory -Force -Path (Join-Path $root 'bin') | Out-Null
$refs = @('PresentationFramework','PresentationCore','WindowsBase') | ForEach-Object { '/reference:' + (Join-Path $wpf ($_ + '.dll')) }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /win32icon:"$root\src\app.ico" /out:"$root\bin\SpeedKlava.exe" /resource:"$root\src\Main.xaml",SpeedKlava.Main.xaml /reference:System.Xaml.dll /reference:System.Xml.dll /reference:System.Core.dll @refs "$root\src\Engine.cs" "$root\src\App.cs"
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed' }
Copy-Item "$root\src\SpeedKlava.exe.config" "$root\bin\SpeedKlava.exe.config" -Force
& $compiler /nologo /target:exe /utf8output /out:"$root\bin\EngineTests.exe" /reference:System.Core.dll /reference:System.Xml.dll "$root\src\Engine.cs" "$root\tests\EngineTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& "$root\bin\EngineTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
Write-Host 'Ready: bin\SpeedKlava.exe'
