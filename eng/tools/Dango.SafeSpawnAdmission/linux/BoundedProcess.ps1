function Invoke-DangoBoundedProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][ValidateRange(1,60000)][int]$TimeoutMilliseconds
    )
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.Start()) { throw 'Could not start the owned query process.' }
        $ownedPid = $process.Id
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($TimeoutMilliseconds)
        if ($timedOut) {
            if (-not $process.HasExited) { $process.Kill($true) }
            if (-not $process.WaitForExit(2000)) { throw 'Timed-out owned query did not exit after termination.' }
        }
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout,$stderr),2000)) {
            throw 'Owned query output did not close within its drain deadline.'
        }
        [pscustomobject]@{ProcessId=$ownedPid;ExitCode=$process.ExitCode;TimedOut=$timedOut;
            Exited=$process.HasExited;WallMilliseconds=$timer.Elapsed.TotalMilliseconds;
            StdOut=$stdout.GetAwaiter().GetResult();StdErr=$stderr.GetAwaiter().GetResult()}
    }
    finally { $process.Dispose() }
}
