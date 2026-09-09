$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildDir = Join-Path $projectRoot "src\TubeJoint.AddIn\bin\Release\net8.0-windows"
$installDir = Join-Path $env:APPDATA "Autodesk\Inventor 2027\Addins\TubeJoint"

if (Get-Process Inventor -ErrorAction SilentlyContinue) {
    throw "Close Autodesk Inventor before installing TubeJoint."
}

if (-not (Test-Path (Join-Path $buildDir "TubeJoint.AddIn.dll"))) {
    throw "Release build not found. Run .\build.ps1 first."
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item (Join-Path $buildDir "TubeJoint.AddIn.dll") $installDir -Force
Copy-Item (Join-Path $buildDir "TubeJoint.AddIn.deps.json") $installDir -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $projectRoot "src\TubeJoint.AddIn\TubeJoint.AddIn.addin") $installDir -Force
Copy-Item (Join-Path $projectRoot "VERSION.txt") $installDir -Force

Write-Host "Installed to $installDir"
Write-Host "Package: iteration11-tube-preparation-ui-v1"
Write-Host "Restart Autodesk Inventor 2027."
