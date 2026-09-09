$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectRoot "src\TubeJoint.AddIn\TubeJoint.AddIn.csproj"

dotnet build $project -c Release

if ($LASTEXITCODE -ne 0) {
    throw "TubeJoint build failed."
}

Write-Host "Build complete: src\TubeJoint.AddIn\bin\Release\net8.0-windows"
Write-Host "Package: iteration11-inventor-style-ui-v1"
