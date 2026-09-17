param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.11f1\Editor\Unity.exe',
    [string]$Distro = 'Ubuntu-22.04',
    [string]$Workspace = '',
    [string]$Vive = 'C:\Program Files\VIVE Business Streaming\RRConsole\RRConsole.exe',
    [string]$SteamVR = '',
    [switch]$SkipRosInstall
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
if (!(Test-Path -LiteralPath $Unity)) { throw 'Install Unity 6000.4.11f1 or pass -Unity with its executable path.' }
$wslHome = (& wsl.exe -d $Distro -- printenv HOME | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or !$wslHome.StartsWith('/')) { throw 'Create the Ubuntu user and verify WSL first.' }
if (!$Workspace) { $Workspace = "$wslHome/surf_ws" }
if (!$Workspace.StartsWith('/') -or $Workspace.Contains('"') -or $Workspace.Contains("`n")) { throw 'Use an absolute Linux workspace path.' }
$linuxRepo = (& wsl.exe -d $Distro -- wslpath -a $repo | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot translate the repository path for WSL.' }
if (!$SkipRosInstall) {
    & wsl.exe -d $Distro -- bash "$linuxRepo/scripts/install-wsl.sh" $Workspace
    if ($LASTEXITCODE -ne 0) { throw 'WSL installation failed. Resolve the error before creating the launcher configuration.' }
}
& wsl.exe -d $Distro -- test -f "$Workspace/install/setup.bash"
if ($LASTEXITCODE -ne 0) { throw 'ROS workspace is not built.' }
& wsl.exe -d $Distro -- test -f "$Workspace/piper_launcher/entry.sh"
if ($LASTEXITCODE -ne 0) { throw 'Launcher backend is missing.' }
if (!$SteamVR) {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if ($steam) { $SteamVR = Join-Path $steam 'steamapps\common\SteamVR\bin\win64\vrstartup.exe' }
    else { $SteamVR = 'C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64\vrstartup.exe' }
}
$cfgPath = Join-Path $repo 'launcher\config.json'
if (Test-Path -LiteralPath $cfgPath) { Copy-Item -LiteralPath $cfgPath -Destination ($cfgPath + '.' + (Get-Date -Format yyyyMMddHHmmss) + '.bak') }
$cfg = @{Unity=$Unity;Project=$repo;Vive=$Vive;SteamVR=$SteamVR;Usbipd='C:\Program Files\usbipd-win\usbipd.exe';Distro=$Distro;Backend="$Workspace/piper_launcher/entry.sh";WslHome=$wslHome}
[IO.File]::WriteAllText($cfgPath,($cfg | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
& "$repo\launcher\build.ps1"
if (!(Test-Path -LiteralPath "$repo\launcher\SURF-Launcher.exe")) { throw 'Launcher build failed' }
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'SURF 一键启动.lnk'))
$shortcut.TargetPath = "$repo\launcher\SURF-Launcher.exe"
$shortcut.WorkingDirectory = "$repo\launcher"
$shortcut.Description = 'SURF Unity / ROS / Piper workbench'
$shortcut.Save()
Write-Output 'Setup complete. Launch the desktop shortcut; use software mode first. Hardware enable is a separate explicit action.'
