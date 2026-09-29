param(
    [ValidateSet('Full','Slim','Both')][string]$Edition = 'Both',
    [switch]$SkipInstaller,
    [string]$Iscc = 'ISCC.exe'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Push-Location $root
try {
    $env:DOTNET_CLI_HOME = Join-Path $root 'artifacts/dotnet-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:NUGET_PACKAGES = Join-Path $root 'artifacts/packages'
    $project = 'src/ClickShow.csproj'
    $editions = if ($Edition -eq 'Both') { @('Full','Slim') } else { @($Edition) }
    foreach ($item in $editions) {
        $selfContained = ($item -eq 'Full').ToString().ToLowerInvariant()
        $publish = Join-Path $root "artifacts/publish/$item"
        # 两套产物完全分离，防止切换版本时混入上次的运行时文件。
        if (Test-Path $publish) {
            $resolved = [IO.Path]::GetFullPath($publish)
            if (!$resolved.StartsWith((Join-Path $root 'artifacts/publish/'), [StringComparison]::OrdinalIgnoreCase)) { throw '发布目录越界' }
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
        dotnet publish $project -c Release -r win-x64 --self-contained $selfContained "-p:WindowsAppSDKSelfContained=$selfContained" -p:PublishSingleFile=false "-p:RestoreConfigFile=$root/NuGet.Config" -o $publish
        if ($LASTEXITCODE) { throw "$item 发布失败" }
        [xml]$csproj = Get-Content $project
        $appVersion = $csproj.Project.PropertyGroup.Version
        $sdkVersion = ($csproj.Project.ItemGroup.PackageReference | Where-Object Include -eq 'Microsoft.WindowsAppSDK').Version
        $info = Get-Content (Join-Path $env:NUGET_PACKAGES "microsoft.windowsappsdk.runtime/$sdkVersion/WindowsAppSDK-VersionInfo.json") | ConvertFrom-Json
        $runtimeConfig = Get-Content (Join-Path $publish 'ClickShow.runtimeconfig.json') | ConvertFrom-Json
        $dotnetVersion = if ($item -eq 'Slim') { $runtimeConfig.runtimeOptions.framework.version } else { '10.0.0' }
        $runtimeVersion = $info.Runtime.Version.String
        if (!$runtimeVersion) { throw '未找到 Windows App SDK 实际运行时版本' }
        $metadata = @"
#define AppVersion "$appVersion"
#define Edition "$item"
#define PublishDir "$publish"
#define DotNetVersion "$dotnetVersion"
#define AppSdkVersion "$runtimeVersion"
"@
        Set-Content -Path 'installer/Build.generated.iss' -Value $metadata -Encoding utf8
        Copy-Item installer/Check-Dependencies.ps1 (Join-Path $publish 'Check-Dependencies.ps1')
        Get-ChildItem $publish -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($publish, $_.FullName) } | Sort-Object | Set-Content (Join-Path $publish 'installed-files.txt') -Encoding utf8
        if (!$SkipInstaller) {
            $compiler = Get-Command $Iscc -ErrorAction SilentlyContinue
            if (!$compiler) {
                $candidate = @(
                    (Join-Path $env:ProgramFiles 'Inno Setup 7/ISCC.exe')
                    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7/ISCC.exe')
                    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe')
                ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
                if ($candidate) { $Iscc = $candidate } else { throw '未找到 Inno Setup。请指定 -Iscc 路径；仅发布使用 -SkipInstaller。' }
            }
            & $Iscc /Qp installer/ClickShow.iss
            if ($LASTEXITCODE) { throw "$item 安装包编译失败" }
        }
    }
} finally { Pop-Location }
