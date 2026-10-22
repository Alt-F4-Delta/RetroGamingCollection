param(
    [Parameter(Mandatory)][string]$OldInstaller,
    [Parameter(Mandatory)][string]$NewInstaller,
    [string]$ExpectedVersion = '1.01',
    [switch]$SkipAppBoot
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$testDir=[IO.Path]::GetFullPath((Join-Path $artifactRoot 'install-smoke'))
if (!$testDir.StartsWith($artifactRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Test installation must remain within artifacts.' }
$uninstallKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{71D14C3E-26E2-4A42-AE08-B52F6C6FA48D}_is1'
$existing=Get-ItemProperty -LiteralPath $uninstallKey -ErrorAction SilentlyContinue
if ($existing -and [IO.Path]::TrimEndingDirectorySeparator($existing.InstallLocation) -ne $testDir) {
    throw 'A real installation already exists. This isolated test will not replace or uninstall it.'
}
function Install-TestVersion([string]$installer,[string]$logName) {
    $resolved=(Resolve-Path -LiteralPath $installer).Path
    $process=Start-Process -FilePath $resolved -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="'+$testDir+'"'),('/LOG="'+(Join-Path $artifactRoot $logName)+'"') -PassThru -Wait -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Installer failed with exit code $($process.ExitCode)." }
}
Install-TestVersion $OldInstaller 'install-old.log'
if (!(Test-Path -LiteralPath (Join-Path $testDir 'installed.flag'))) { throw 'Installed marker missing.' }
$localConfig=Join-Path $testDir 'Configuration'
New-Item -ItemType Directory -Path $localConfig -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $localConfig 'keep-settings.json'),'preserve-existing-settings')
$dataRoot=Join-Path $artifactRoot 'test-user-data'
New-Item -ItemType Directory -Force -Path (Join-Path $dataRoot 'Configuration'),(Join-Path $dataRoot 'Saves') | Out-Null
$settingsFile=Join-Path $dataRoot 'Configuration/settings.json'
$saveFile=Join-Path $dataRoot 'Saves/preserve.txt'
[IO.File]::WriteAllText($settingsFile,'{"LibraryPaths":[],"LanguageFilter":"Untagged","UsaOnly":true}')
[IO.File]::WriteAllText($saveFile,'preserve-original-save-fixture')
Install-TestVersion $NewInstaller 'install-upgrade.log'
if ([IO.File]::ReadAllText((Join-Path $localConfig 'keep-settings.json')) -ne 'preserve-existing-settings' -or [IO.File]::ReadAllText($saveFile) -ne 'preserve-original-save-fixture') { throw 'Upgrade altered user fixtures.' }
Write-Host 'PASS: Installer upgrade preserved settings and saves.'
$originalDataRoot=$env:RETROGAMING_DATA_DIRECTORY
$testApp=$null
if ($SkipAppBoot) {
    $productVersion=((Get-Item -LiteralPath (Join-Path $testDir 'RetroGaming.exe')).VersionInfo.ProductVersion -split '\+')[0]
    if ($productVersion -ne $ExpectedVersion) { throw "Installed binary version is $productVersion, expected $ExpectedVersion." }
    Write-Host "PASS: Installed binary is version $ExpectedVersion (headless build host)."
} else {
try {
    $env:RETROGAMING_DATA_DIRECTORY=$dataRoot
    $testApp=Start-Process -FilePath (Join-Path $testDir 'RetroGaming.exe') -PassThru -WindowStyle Hidden
    $ready=$false
    for ($i=0;$i -lt 30;$i++) {
        Start-Sleep -Milliseconds 300
        $testApp.Refresh()
        if ($testApp.HasExited) { throw 'Installed app exited unexpectedly.' }
        if ($testApp.MainWindowTitle -like "*v$ExpectedVersion") { $ready=$true;break }
    }
    if (!$ready) { throw "Installed app did not show version $ExpectedVersion." }
    Write-Host "PASS: Installed app booted as $($testApp.MainWindowTitle)."
} finally {
    $env:RETROGAMING_DATA_DIRECTORY=$originalDataRoot
    if ($testApp -and !$testApp.HasExited) { Stop-Process -Id $testApp.Id }
}
}
$uninstaller=Join-Path $testDir 'unins000.exe'
$uninstall=Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -PassThru -Wait -WindowStyle Hidden
if ($uninstall.ExitCode -ne 0) { throw "Test uninstallation failed: $($uninstall.ExitCode)" }
if (Test-Path -LiteralPath (Join-Path $testDir 'RetroGaming.exe')) { throw 'Test app was not uninstalled.' }
if ([IO.File]::ReadAllText($saveFile) -ne 'preserve-original-save-fixture') { throw 'Uninstallation touched user data.' }
Write-Host 'PASS: Isolated uninstall removed program files and retained user data.'
