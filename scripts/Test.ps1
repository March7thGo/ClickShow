param([switch]$Runtime, [switch]$Stress)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Push-Location $root
try {
    $env:DOTNET_CLI_HOME = Join-Path $root 'artifacts/dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $root 'artifacts/packages'
    dotnet run --project tests/ClickShow.Tests.csproj "-p:RestoreConfigFile=$root/NuGet.Config"
    if ($LASTEXITCODE) { throw '逻辑测试失败' }
    if ($Runtime -or $Stress) {
        $executable = Join-Path $root 'artifacts/publish/Full/ClickShow.exe'
        if (!(Test-Path $executable)) { throw '请先运行 scripts/Build.ps1 -Edition Full -SkipInstaller' }
        $mode = if ($Stress) { '--stress' } else { '--diagnostics' }
        $log = Join-Path (Split-Path $executable) 'diagnostics.log'
        $offset = if (Test-Path $log) { (Get-Item $log).Length } else { 0 }
        $process = Start-Process $executable -ArgumentList $mode,'--silent' -WindowStyle Hidden -PassThru
        $timeout = if ($Stress) { 90000 } else { 15000 }
        if (!$process.WaitForExit($timeout)) { throw "诊断超时，进程 $($process.Id) 仍在运行" }
        if ($process.ExitCode -ne 0) { throw "诊断退出码 $($process.ExitCode)" }
        $bytes = [IO.File]::ReadAllBytes($log)
        $result = [Text.Encoding]::UTF8.GetString($bytes, $offset, $bytes.Length - $offset)
        Write-Output $result
        if ($result -match 'ERROR:' -or $result -notmatch 'Completed requests=') { throw '运行诊断未完成；检查是否已有 ClickShow 实例或查看日志' }
    }
} finally { Pop-Location }
