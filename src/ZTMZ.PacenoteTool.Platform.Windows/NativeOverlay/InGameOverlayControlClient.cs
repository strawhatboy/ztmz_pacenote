using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace ZTMZ.PacenoteTool.Platform.Windows.NativeOverlay;

/// <summary>Controls an already loaded overlay. Does not inject or decide game eligibility.</summary>
public sealed class InGameOverlayControlClient
{
    private const int MessageLimit = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly int processId;
    private readonly string instance;
    private readonly TimeSpan timeout;
    private readonly SemaphoreSlim gate = new(1, 1);

    public InGameOverlayControlClient(Process process, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        processId = process.Id;
        // Capture creation time now; do not trust a later process that reuses the PID.
        instance = string.Create(CultureInfo.InvariantCulture, $"{processId}:{process.StartTime.ToUniversalTime().ToFileTimeUtc()}");
        this.timeout = timeout ?? TimeSpan.FromSeconds(5);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public Task<InGameOverlayStatus> QueryAsync(CancellationToken cancellationToken = default) =>
        SendAsync("status", 0, "auto", cancellationToken);

    public Task<InGameOverlayStatus> StartAsync(int port, string api = "auto", CancellationToken cancellationToken = default, ulong targetWindow = 0)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (api is not ("auto" or "dx9" or "dx10" or "dx11" or "dx12" or "vulkan"))
            throw new ArgumentException("Unknown rendering API.", nameof(api));
        return SendAsync("start", port, api, cancellationToken, targetWindow);
    }

    public Task<InGameOverlayStatus> StopAsync(CancellationToken cancellationToken = default) =>
        SendAsync("stop", 0, "auto", cancellationToken);

    private async Task<InGameOverlayStatus> SendAsync(string command, int port, string api, CancellationToken cancellationToken, ulong targetWindow = 0)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        bool entered = false;
        try
        {
            await gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            using var pipe = new NamedPipeClientStream(".", $"ztmz-dashboard-{processId}", PipeDirection.InOut,
                PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint server) || server != (uint)processId)
                throw new InvalidDataException("Overlay control endpoint belongs to another process.");
            // Verify process incarnation before issuing a mutation, not only after its reply.
            using (var target = Process.GetProcessById(processId))
            {
                string actual = string.Create(CultureInfo.InvariantCulture, $"{processId}:{target.StartTime.ToUniversalTime().ToFileTimeUtc()}");
                if (actual != instance) throw new InvalidDataException("Overlay process was replaced.");
            }
            pipe.ReadMode = PipeTransmissionMode.Message;
            byte[] request = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, command, instance, port, api, targetWindow }, JsonOptions);
            await pipe.WriteAsync(request, deadline.Token).ConfigureAwait(false);
            byte[] buffer = new byte[MessageLimit];
            int length = 0;
            do
            {
                if (length == buffer.Length) throw new InvalidDataException("Overlay control response exceeds the size limit.");
                int read = await pipe.ReadAsync(buffer.AsMemory(length), deadline.Token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("Overlay control disconnected before replying.");
                length += read;
            } while (!pipe.IsMessageComplete);
            await pipe.WriteAsync(new byte[] { 1 }, deadline.Token).ConfigureAwait(false);
            var status = JsonSerializer.Deserialize<InGameOverlayStatus>(buffer.AsSpan(0, length), JsonOptions)
                ?? throw new InvalidDataException("Missing overlay status.");
            if (status.Version != 1 || status.ModuleVersion != 1 || status.Pid != processId || status.Instance != instance)
                throw new InvalidDataException("Unexpected overlay protocol or process instance.");
            if (status.RenderError is null || status.RequestedApi is null || status.SelectedApi is null || status.LastFrameAgeMs < -1)
                throw new InvalidDataException("Invalid overlay status fields.");
            if (!status.Ok) throw new InvalidOperationException(status.Error ?? "Overlay command rejected.");
            return status;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timed-out mutation may have completed. Query state before deciding to retry.
            throw new TimeoutException("Overlay control request timed out; its result is unknown.");
        }
        finally { if (entered) gate.Release(); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}

public sealed record InGameOverlayStatus
{
    public required int Version { get; init; }
    public required int ModuleVersion { get; init; }
    public required int Pid { get; init; }
    public required string Instance { get; init; }
    public required bool Ok { get; init; }
    public required bool Started { get; init; }
    public required int Port { get; init; }
    public required string RequestedApi { get; init; }
    public required string SelectedApi { get; init; }
    public required ulong SubmittedFrames { get; init; }
    public required ulong Window { get; init; }
    public required long LastFrameAgeMs { get; init; }
    public required string RenderError { get; init; }
    public string? Error { get; init; }
    public string[] AvailableApis { get; init; } = [];
    public string[] CandidateApis { get; init; } = [];
    public string[] HookWarnings { get; init; } = [];
    public string QueueSource { get; init; } = "";
    private readonly long receivedAt = Stopwatch.GetTimestamp();

    // A snapshot expires even if the caller stops polling. This does not prove telemetry arrival.
    public bool HasRecentSubmission => Started && SelectedApi is ("dx9" or "dx10" or "dx11" or "dx12" or "vulkan") && Window != 0 && SubmittedFrames > 0
        && RenderError.Length == 0 && LastFrameAgeMs >= 0
        && LastFrameAgeMs + Stopwatch.GetElapsedTime(receivedAt).TotalMilliseconds < 1000;
}
