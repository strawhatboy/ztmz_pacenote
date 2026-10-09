param([Parameter(Mandatory)][string]$OutputDirectory, [int]$Port = 20778, [int]$Minutes = 30)
$ErrorActionPreference = 'Stop'
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$udp = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, $Port))
$udp.Client.ReceiveTimeout = 1000
$stream = [IO.File]::Open((Join-Path $OutputDirectory 'telemetry.bin'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
$writer = [IO.BinaryWriter]::new($stream)
$status = [IO.StreamWriter]::new((Join-Path $OutputDirectory 'capture.log'), $false)
$status.AutoFlush = $true
$count = 0
$sizes = @{}
try {
    # File header; each record: UTC ticks int64, packet length int32, original UDP bytes.
    $writer.Write([Text.Encoding]::ASCII.GetBytes('WRCGUDP1'))
    $writer.Flush()
    $status.WriteLine("READY 127.0.0.1:$Port PID=$PID UTC=$([DateTime]::UtcNow.ToString('O')) duration=${Minutes}m")
    $until = [DateTime]::UtcNow.AddMinutes($Minutes)
    while ([DateTime]::UtcNow -lt $until) {
        $remote = [Net.IPEndPoint]::new([Net.IPAddress]::Any, 0)
        try { $bytes = $udp.Receive([ref]$remote) }
        catch [Net.Sockets.SocketException] {
            if ($_.Exception.SocketErrorCode -eq [Net.Sockets.SocketError]::TimedOut) { continue }
            throw
        }
        $writer.Write([DateTime]::UtcNow.Ticks)
        $writer.Write([int]$bytes.Length)
        $writer.Write([byte[]]$bytes)
        $count++
        if (!$sizes.ContainsKey($bytes.Length)) {
            $sizes[$bytes.Length] = $true
            $status.WriteLine("PACKET size=$($bytes.Length) source=$remote UTC=$([DateTime]::UtcNow.ToString('O'))")
        }
        if ($count % 60 -eq 0) { $writer.Flush() }
        if ($count % 600 -eq 0) { $status.WriteLine("RECEIVED $count UTC=$([DateTime]::UtcNow.ToString('O'))") }
    }
} finally {
    $status.WriteLine("STOP count=$count UTC=$([DateTime]::UtcNow.ToString('O'))")
    $writer.Dispose()
    $udp.Dispose()
    $status.Dispose()
}
