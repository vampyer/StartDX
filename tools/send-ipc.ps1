<#
  Send one message to a running StartDX dock over its named pipe and print the replies.
  Handy for scripting, hotkey daemons, and debugging the protocol.

  Examples
    .\send-ipc.ps1 -Type setTheme -Payload '{"theme":"PremiumDark"}'
    .\send-ipc.ps1 -Type patchSettings -Payload '{"edge":"Left","thicknessDip":72}'
    .\send-ipc.ps1 -Type notify -Payload '{"title":"Build finished","body":"All green","source":"CI"}'
    .\send-ipc.ps1 -Type command -Payload '{"name":"toggleStart"}'
    .\send-ipc.ps1 -Type getState
#>
param(
    [Parameter(Mandatory)][string]$Type,
    [string]$Payload = "",
    [int]$WaitMs = 1500
)

$pipeName = "StartDX.Ipc.v1.$env:USERNAME"
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $pipeName, [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
try { $pipe.Connect(3000) } catch { Write-Error "Dock is not running (pipe '$pipeName' not found)."; return }

$utf8 = New-Object System.Text.UTF8Encoding($false)
$reader = New-Object System.IO.StreamReader($pipe, $utf8)
$writer = New-Object System.IO.StreamWriter($pipe, $utf8)
$writer.NewLine = "`n"; $writer.AutoFlush = $true

$id = [guid]::NewGuid().ToString("N").Substring(0, 8)
$msg = if ($Payload) { "{`"v`":1,`"type`":`"$Type`",`"id`":`"$id`",`"payload`":$Payload}" } else { "{`"v`":1,`"type`":`"$Type`",`"id`":`"$id`"}" }
$writer.WriteLine($msg)

$deadline = (Get-Date).AddMilliseconds($WaitMs)
$pending = $null
while ((Get-Date) -lt $deadline) {
    if ($null -eq $pending) { $pending = $reader.ReadLineAsync() }
    if ($pending.Wait(200)) {
        if ($pending.Result) { $pending.Result }
        $pending = $null
    }
}
$pipe.Dispose()
