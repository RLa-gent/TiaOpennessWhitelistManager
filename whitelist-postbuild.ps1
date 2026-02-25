#Requires -Version 5.1
<#
.SYNOPSIS
    Sends a built executable to the TiaOpennessWhitelistManager named pipe so it is
    automatically added to the TIA Portal Openness whitelist after each build.

.DESCRIPTION
    The TiaOpennessWhitelistManager service must already be running (as Administrator)
    before the build that triggers this script.

.PARAMETER ExecutablePath
    Absolute path of the .exe to whitelist.  In a Visual Studio post-build event use
    the $(TargetPath) macro, e.g.:
        powershell -ExecutionPolicy Bypass -File "$(ProjectDir)whitelist-postbuild.ps1" `
            -ExecutablePath "$(TargetPath)" -TiaVersion "18.0"

.PARAMETER TiaVersion
    TIA Portal version string, e.g. "18.0" or "21.0".

.EXAMPLE
    .\whitelist-postbuild.ps1 -ExecutablePath "C:\MyApp\MyApp.exe" -TiaVersion "18.0"
#>
param(
    [Parameter(Mandatory)]
    [string] $ExecutablePath,

    [Parameter(Mandatory)]
    [string] $TiaVersion
)

Add-Type -AssemblyName System.Core

$pipeName  = 'TiaOpennessWhitelistPipe'
$timeoutMs = 5000

$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(
    '.',
    $pipeName,
    [System.IO.Pipes.PipeDirection]::InOut,
    [System.IO.Pipes.PipeOptions]::None
)

try {
    Write-Host "Connecting to named pipe '$pipeName'..."
    $pipe.Connect($timeoutMs)

    $writer            = New-Object System.IO.StreamWriter($pipe)
    $writer.AutoFlush  = $true
    $writer.WriteLine($ExecutablePath)
    $writer.WriteLine($TiaVersion)

    $reader = New-Object System.IO.StreamReader($pipe)
    $result = $reader.ReadLine()

    Write-Host $result

    if ($result -notlike 'Success:*') {
        exit 1
    }
}
catch [System.TimeoutException] {
    Write-Error "Timed out waiting for '$pipeName'. Is TiaOpennessWhitelistManager running as Administrator?"
    exit 1
}
catch {
    Write-Error "Pipe error: $_"
    exit 1
}
finally {
    $pipe.Dispose()
}
