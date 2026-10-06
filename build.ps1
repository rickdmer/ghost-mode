# Builds bin\GhostMode.exe with the C# compiler that ships with Windows (.NET Framework 4.8) - no SDK needed.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null

if (Get-Process GhostMode -ErrorAction SilentlyContinue) { throw 'Ghost Mode is running; close it before building.' }

$refs = 'WPF\PresentationFramework.dll', 'WPF\PresentationCore.dll', 'WPF\WindowsBase.dll', 'System.Xaml.dll',
        'WPF\UIAutomationClient.dll', 'WPF\UIAutomationTypes.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
        'System.Runtime.Serialization.dll', 'System.Core.dll', 'System.dll', 'Microsoft.CSharp.dll' | ForEach-Object { "/r:$fw\$_" }
$sources = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | ForEach-Object FullName

& "$fw\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu /out:"$bin\GhostMode.exe" `
    /win32icon:"$root\assets\app.ico" /win32manifest:"$root\src\app.manifest" `
    /resource:"$root\src\MainWindow.xaml,GhostMode.MainWindow.xaml" /resource:"$root\assets\app.ico,GhostMode.app.ico" `
    @refs @sources 2>&1 | Where-Object { $_ -notmatch 'only supports language versions up to C# 5|This compiler is provided|For compilers that support newer|https://go.microsoft.com/fwlink/\?LinkID=533240|^\s*$' }
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
Copy-Item "$root\src\App.config" "$bin\GhostMode.exe.config" -Force
"built $bin\GhostMode.exe"

