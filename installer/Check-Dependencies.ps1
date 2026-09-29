param([string]$DotNetVersion, [string]$AppSdkVersion)
$ErrorActionPreference = 'Stop'
try {
    $minimum = [version]$DotNetVersion
    $location = (Get-ItemProperty 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64' -ErrorAction SilentlyContinue).InstallLocation
    if (!$location) { $location = Join-Path $env:ProgramFiles 'dotnet' }
    $shared = Join-Path $location 'shared/Microsoft.NETCore.App'
    $dotnet = @(Get-ChildItem -LiteralPath $shared -Directory -ErrorAction SilentlyContinue | Where-Object {
        $version = $null
        [version]::TryParse($_.Name, [ref]$version) -and $version.Major -eq $minimum.Major -and $version.Minor -eq $minimum.Minor -and $version -ge $minimum
    })
    if ($dotnet.Count -eq 0) { exit 10 }
    $runtime = @(Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.1.8' | Where-Object { $_.PublisherId -eq '8wekyb3d8bbwe' -and $_.Architecture -eq 'X64' -and [version]$_.Version -ge [version]$AppSdkVersion })
    if ($runtime.Count -eq 0) { exit 20 }
    exit 0
} catch { exit 30 }
