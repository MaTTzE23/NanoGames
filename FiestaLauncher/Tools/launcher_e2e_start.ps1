$ErrorActionPreference = 'Stop'

$payloadFile = Join-Path $env:TEMP 'nanonline_launcher_e2e.json'
if (-not (Test-Path $payloadFile)) {
    throw 'Auth payload file is missing.'
}

$data = Get-Content $payloadFile -Raw | ConvertFrom-Json

function Send-TrayCommand([object]$Command) {
    $client = [System.IO.Pipes.NamedPipeClientStream]::new('.', 'NanOnline.SecurityTray.v1', [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $connectTask = $client.ConnectAsync(3000)
        if (-not $connectTask.Wait(4000)) {
            throw 'Security tray pipe connection timed out.'
        }

        $utf8 = [System.Text.UTF8Encoding]::new($false)
        $writer = [System.IO.StreamWriter]::new($client, $utf8, 4096, $true)
        $writer.AutoFlush = $true
        $reader = [System.IO.StreamReader]::new($client, $utf8, $false, 4096, $true)
        try {
            $writer.WriteLine(($Command | ConvertTo-Json -Depth 10 -Compress))
            $readTask = $reader.ReadLineAsync()
            if (-not $readTask.Wait(6000)) {
                throw 'Security tray response timed out.'
            }

            $line = $readTask.Result
            if ([string]::IsNullOrWhiteSpace($line)) {
                throw 'Security tray returned no response.'
            }

            return $line | ConvertFrom-Json
        }
        finally {
            $writer.Dispose()
            $reader.Dispose()
        }
    }
    finally {
        $client.Dispose()
    }
}

Get-Process NanoSecurityTray -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

$trayPath = Join-Path $data.ClientRoot $data.Config.SecurityTrayExecutable
Write-Output 'STAGE=TRAY_START'
$trayProcess = Start-Process -FilePath $trayPath -WorkingDirectory $data.ClientRoot -PassThru
$trayReady = $false
for ($i = 0; $i -lt 25; $i++) {
    try {
        $pong = Send-TrayCommand ([pscustomobject]@{ command = 'ping' })
        if ($pong.Success) {
            $trayReady = $true
            break
        }
    }
    catch {
    }

    Start-Sleep -Milliseconds 300
}

if (-not $trayReady) {
    throw 'Security tray pipe did not come online.'
}

Write-Output 'STAGE=TRAY_UPSERT'
$upsert = Send-TrayCommand ([pscustomobject]@{
    command = 'upsert_session'
    accessToken = [string]$data.AccessToken
    startToken = [string]$data.StartToken
    heartbeatUrl = [string]$data.Config.LauncherHeartbeatUrl
    securityEventUrl = [string]$data.Config.LauncherSecurityEventUrl
    clientRoot = [string]$data.ClientRoot
    gameProcessName = [System.IO.Path]::GetFileNameWithoutExtension([string]$data.Config.GameBinaryExecutable)
    expectedHashes = $data.ExpectedHashes
    heartbeatIntervalSeconds = 30
})
if (-not $upsert.Success) {
    throw "Tray upsert failed: $($upsert.Message)"
}

$watch = Send-TrayCommand ([pscustomobject]@{ command = 'start_watch' })
if (-not $watch.Success) {
    throw "Tray watch failed: $($watch.Message)"
}

Write-Output 'STAGE=WRAPPER_START'
$launchId = [guid]::NewGuid().ToString('N')
$pipeName = 'NanOnline.StartPayload.' + $launchId
$server = [System.IO.Pipes.NamedPipeServerStream]::new($pipeName, [System.IO.Pipes.PipeDirection]::Out, 1, [System.IO.Pipes.PipeTransmissionMode]::Byte, [System.IO.Pipes.PipeOptions]::Asynchronous)

try {
    $wrapperPath = Join-Path $data.ClientRoot $data.Config.WrapperExecutable
    $wrapper = Start-Process -FilePath $wrapperPath -ArgumentList "--launch-id $launchId" -WorkingDirectory $data.ClientRoot -PassThru -WindowStyle Hidden
    $waitTask = $server.WaitForConnectionAsync()
    if (-not $waitTask.Wait(15000)) {
        throw 'Wrapper did not connect to the launch pipe.'
    }

    $payload = [ordered]@{
        launchId = $launchId
        loginHost = [string]$data.LoginHost
        loginPort = [int]$data.LoginPort
        oskServer = [string]$data.OskServer
        oskStore = [string]$data.OskStore
        startToken = [string]$data.StartToken
        clientRoot = [string]$data.ClientRoot
        gameBinary = [string]$data.Config.GameBinaryExecutable
    } | ConvertTo-Json -Compress

    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $writer = [System.IO.StreamWriter]::new($server, $utf8, 4096, $true)
    try {
        $writer.Write($payload)
        $writer.Flush()
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $server.Dispose()
}

$wrapperExited = $wrapper.WaitForExit(12000)
Start-Sleep -Seconds 5
$wrapper.Refresh()

$gamePath = Join-Path $data.ClientRoot $data.Config.GameBinaryExecutable
$gameProcess = Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -and $_.ExecutablePath -ieq $gamePath
} | Select-Object -First 1

$wrapperLog = Join-Path $data.ClientRoot 'nano_wrapper.log'
$trayLogInstalled = Join-Path $data.ClientRoot 'nano_security_tray.log'
$trayLogLocal = 'C:\Users\Administrator\AppData\Local\NanOnline\Logs\nano_security_tray.log'
$trayLog = if (Test-Path $trayLogInstalled) { $trayLogInstalled } elseif (Test-Path $trayLogLocal) { $trayLogLocal } else { $null }

"TRAY_PID=$($trayProcess.Id)"
"UPSERT_SUCCESS=$($upsert.Success)"
"WATCH_SUCCESS=$($watch.Success)"
"WRAPPER_PID=$($wrapper.Id)"
"WRAPPER_EXITED=$wrapperExited"
"WRAPPER_EXITCODE=$($wrapper.ExitCode)"
"GAME_RUNNING=$([bool]($null -ne $gameProcess))"
if ($gameProcess) {
    "GAME_PID=$($gameProcess.ProcessId)"
}
"WRAPPER_LOG_PATH=$wrapperLog"
if (Test-Path $wrapperLog) {
    Get-Content $wrapperLog -Tail 20
}

if ($trayLog) {
    "TRAY_LOG_PATH=$trayLog"
    Get-Content $trayLog -Tail 20
}
else {
    "TRAY_LOG_PATH=MISSING"
}

Remove-Item $payloadFile -ErrorAction SilentlyContinue
