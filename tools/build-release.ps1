param(
    [int]$Counter = 0,
    [string]$InnoCompiler = '',
    [string]$ArtifactRoot = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if ($Counter -eq 0) { $Counter = [int](Get-Content -LiteralPath (Join-Path $projectRoot 'version.txt') -Raw).Trim() }
if ($Counter -lt 100 -or $Counter -gt 6553599) { throw 'Version counter must be between 100 and 6553599.' }
$major = [int][Math]::Floor($Counter / 100)
$minor = $Counter % 100
$display = '{0}.{1:D2}' -f $major,$minor
$numeric = "$major.$minor.0.0"
if (!$ArtifactRoot) { $ArtifactRoot = Join-Path $projectRoot 'artifacts' }
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$appDir = Join-Path $ArtifactRoot "app-$display"
$releaseDir = Join-Path $ArtifactRoot "release-$display"
New-Item -ItemType Directory -Force -Path $appDir,$releaseDir | Out-Null
if (!$InnoCompiler) {
    $compilerRoot = Join-Path $projectRoot 'Dependencies/InnoSetup'
    $InnoCompiler = Join-Path $compilerRoot 'tools/ISCC.exe'
    if (!(Test-Path -LiteralPath $InnoCompiler)) {
        New-Item -ItemType Directory -Force -Path $compilerRoot | Out-Null
        $package = Join-Path $compilerRoot 'compiler.zip'
        Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/tools.innosetup/6.7.3/tools.innosetup.6.7.3.nupkg' -OutFile $package
        Expand-Archive -LiteralPath $package -DestinationPath $compilerRoot -Force
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $InnoCompiler
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Pyrsys B\.V\.') { throw 'Inno compiler publisher verification failed.' }
}
& dotnet publish (Join-Path $projectRoot 'RetroGamingCollection.csproj') -c Release -r win-x64 --self-contained true -o $appDir "-p:Version=$major.$minor.0" "-p:InformationalVersion=$display" "-p:FileVersion=$numeric" "-p:AssemblyVersion=$numeric" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'),(Join-Path $projectRoot 'CHANGELOG.md') -Destination $appDir
& $InnoCompiler /Qp "/DAppVersion=$display" "/DNumericVersion=$numeric" "/DSourceDir=$appDir" "/DOutputDir=$releaseDir" (Join-Path $projectRoot 'installer/RetroGamingCollection.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$zip = Join-Path $releaseDir "RetroGamingCollection-Portable-$display-win-x64.zip"
Compress-Archive -Path (Join-Path $appDir '*') -DestinationPath $zip -Force
$sums = Get-ChildItem -LiteralPath $releaseDir -File | Where-Object Extension -In '.exe','.zip' | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[IO.File]::WriteAllLines((Join-Path $releaseDir 'SHA256SUMS.txt'), $sums, [Text.UTF8Encoding]::new($false))
Write-Host "Built version $display at $releaseDir"
if ($env:GITHUB_OUTPUT) {
    "version=$display" >> $env:GITHUB_OUTPUT
    "release-dir=$releaseDir" >> $env:GITHUB_OUTPUT
}
