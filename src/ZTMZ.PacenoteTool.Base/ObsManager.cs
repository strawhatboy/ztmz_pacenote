// To record video with OBS

using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;

namespace ZTMZ.PacenoteTool.Base;

public enum ObsConnectionState
{
    Disabled,
    Disconnected,
    Connecting,
    Connected,
    Failed
}

public sealed record ObsConnectionStatus(ObsConnectionState State, string ErrorMessage)
{
    public bool IsConnected => State == ObsConnectionState.Connected;
}

public class ObsManager : IDisposable {
    private readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static ObsManager _instance;
    private static readonly object _lock = new object();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly object _connectionStateLock = new();

    private OBSWebsocket _obs;
    private ObsConnectionStatus _connectionStatus = new(ObsConnectionState.Disabled, string.Empty);

    private ObsManager() { }

    public event Action RecordStarted;
    public event Action RecordStopped;
    public event Action<ObsConnectionStatus> ConnectionStatusChanged;

    public ObsConnectionStatus ConnectionStatus
    {
        get
        {
            lock (_connectionStateLock)
            {
                return _connectionStatus;
            }
        }
    }

    public static ObsManager Instance {
        get {
            lock (_lock) {
                if (_instance == null) {
                    _instance = new ObsManager();
                }
                return _instance;
            }
        }
    }

    public void RefreshConnectionStatus()
    {
        if (!Config.Instance.ReplayOBSSave)
        {
            SetConnectionStatus(ObsConnectionState.Disabled);
            return;
        }

        if (_obs?.IsConnected == true)
        {
            SetConnectionStatus(ObsConnectionState.Connected);
            return;
        }

        if (ConnectionStatus.State != ObsConnectionState.Connecting)
        {
            SetConnectionStatus(ObsConnectionState.Disconnected);
        }
    }

    public async Task<bool> Connect() {
        if (!Config.Instance.ReplayOBSSave) {
            _logger.Info("OBS recording is disabled wont connect to OBS");
            SetConnectionStatus(ObsConnectionState.Disabled);
            return false;
        }

        await _connectGate.WaitAsync().ConfigureAwait(false);
        OBSWebsocket connection = null;
        try {
            if (!Config.Instance.ReplayOBSSave)
            {
                SetConnectionStatus(ObsConnectionState.Disabled);
                return false;
            }

            if (_obs?.IsConnected == true) {
                SetConnectionStatus(ObsConnectionState.Connected);
                return true;
            }

            _logger.Info("Connecting to OBS");
            SetConnectionStatus(ObsConnectionState.Connecting);

            var timeout = Math.Max(1, Config.Instance.ReplayOBSWebsocketTimeout);
            var endpointFailure = await ProbeWebSocketEndpointAsync(
                Config.Instance.ReplayOBSWebsocketUrl,
                timeout).ConfigureAwait(false);
            if (endpointFailure != null)
            {
                _logger.Error("Failed to connect to OBS: {0}", endpointFailure);
                SetConnectionStatus(ObsConnectionState.Failed, endpointFailure);
                return false;
            }

            var previousConnection = _obs;
            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection = new OBSWebsocket();
            connection.Connected += (_, _) => {
                if (!ReferenceEquals(_obs, connection))
                {
                    return;
                }

                _logger.Info("OBS connected!");
                SetConnectionStatus(ObsConnectionState.Connected);
                connected.TrySetResult(true);
            };
            connection.Disconnected += (_, _) => {
                if (!ReferenceEquals(_obs, connection))
                {
                    return;
                }

                SetConnectionStatus(
                    Config.Instance.ReplayOBSSave
                        ? ObsConnectionState.Disconnected
                        : ObsConnectionState.Disabled);
            };
            connection.RecordStateChanged += RecordStateChanged;
            _obs = connection;
            DisconnectQuietly(previousConnection);

            connection.ConnectAsync(Config.Instance.ReplayOBSWebsocketUrl, Config.Instance.ReplayOBSWebsocketPassword);
            var didConnect = await Task.WhenAny(connected.Task, Task.Delay(timeout)).ConfigureAwait(false) == connected.Task;
            if (!didConnect) {
                if (ReferenceEquals(_obs, connection))
                {
                    _obs = null;
                }

                DisconnectQuietly(connection);
                var message = $"Connection timed out after {timeout} ms.";
                _logger.Error("Failed to connect to OBS because of timeout");
                SetConnectionStatus(ObsConnectionState.Failed, message);
                return false;
            }

            if (!connection.IsConnected)
            {
                if (ReferenceEquals(_obs, connection))
                {
                    _obs = null;
                }

                DisconnectQuietly(connection);
                SetConnectionStatus(ObsConnectionState.Disconnected);
                return false;
            }

            SetConnectionStatus(ObsConnectionState.Connected);
            return true;
        } catch (Exception ex) {
            if (ReferenceEquals(_obs, connection))
            {
                _obs = null;
            }

            DisconnectQuietly(connection);
            var message = GetConnectionErrorMessage(ex);
            _logger.Error(ex, "Failed to connect to OBS");
            SetConnectionStatus(ObsConnectionState.Failed, message);
            return false;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private void RecordStateChanged(object sender, RecordStateChangedEventArgs e) {
        _logger.Info($"OBS record state changed to {e.OutputState}");
        if (e.OutputState.State == OutputState.OBS_WEBSOCKET_OUTPUT_STARTED) {
            RecordStarted?.Invoke();
        } else if (e.OutputState.State == OutputState.OBS_WEBSOCKET_OUTPUT_STOPPED) {
            RecordStopped?.Invoke();
        }
    }

    public async void StartRecording() {
        if (!Config.Instance.ReplayOBSSave) {
            _logger.Info("OBS recording is disabled wont start recording video with OBS");
            return;
        }

        _logger.Info("Start recording video with OBS");
        // Start recording video
        if (await Connect()) {
            var connection = _obs;
            if (connection?.IsConnected != true)
            {
                SetConnectionStatus(ObsConnectionState.Disconnected);
                _logger.Error("OBS disconnected before recording could start.");
                return;
            }

            _logger.Info("OBS connected. start recording video with OBS");
            try {
                connection.StartRecord();
                _logger.Info("Recording video with OBS started");
            } catch (Exception ex) {
                _logger.Error($"Failed to start recording video with OBS because of \n{ex.ToString()}");
            }
        } else {
            _logger.Error("Failed to connect to OBS");
        }
    }

    public string AbnormallyStopRecording() {
        var video_path = StopRecording();
        if (!string.IsNullOrEmpty(video_path) && Config.Instance.ReplayCleanUpAbnormalVideo) {
            try {
                System.IO.File.Delete(video_path);
                _logger.Info($"Deleted video at {video_path} because of abnormal stop");
            } catch (Exception ex) {
                _logger.Error($"Failed to delete video at {video_path} because of \n{ex.ToString()}");
            }
        }
        return video_path;
    }

    public string StopRecording() {
        if (!Config.Instance.ReplayOBSSave) {
            _logger.Info("OBS recording is disabled wont start recording video with OBS");
            return "";
        }
        
        _logger.Info("Stop recording video with OBS");
        var connection = _obs;
        if (connection?.IsConnected == true) {
            _logger.Info("OBS connected. stop recording video with OBS");
            try {
                var output_path = connection.StopRecord();
                _logger.Info($"Video saved at {output_path}");
                return output_path;
            } catch (Exception ex) {
                _logger.Error($"Failed to stop recording video with OBS because of \n{ex.ToString()}");
                return "";
            }
        } else {
            SetConnectionStatus(
                Config.Instance.ReplayOBSSave
                    ? ObsConnectionState.Disconnected
                    : ObsConnectionState.Disabled);
            _logger.Error("OBS not connected!");
            return "";
        }
    }

    public void Dispose()
    {
        var connection = _obs;
        _obs = null;
        DisconnectQuietly(connection);
        SetConnectionStatus(ObsConnectionState.Disabled);
    }

    private void SetConnectionStatus(ObsConnectionState state, string errorMessage = "")
    {
        var status = new ObsConnectionStatus(state, errorMessage);
        lock (_connectionStateLock)
        {
            if (_connectionStatus == status)
            {
                return;
            }

            _connectionStatus = status;
        }

        try
        {
            ConnectionStatusChanged?.Invoke(status);
        }
        catch (Exception exception)
        {
            _logger.Warn(exception, "OBS connection status listener failed.");
        }
    }

    private void DisconnectQuietly(OBSWebsocket connection)
    {
        if (connection == null)
        {
            return;
        }

        try
        {
            connection.Disconnect();
        }
        catch (Exception exception)
        {
            _logger.Debug(exception, "Failed to disconnect OBS websocket.");
        }
    }

    private static string GetConnectionErrorMessage(Exception exception)
    {
        return string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message.ReplaceLineEndings(" ");
    }

    private static async Task<string> ProbeWebSocketEndpointAsync(string endpoint, int timeoutMilliseconds)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || !endpointUri.Scheme.Equals(Uri.UriSchemeWs, StringComparison.OrdinalIgnoreCase))
        {
            return "Invalid OBS WebSocket URL.";
        }

        using var client = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        try
        {
            await client.ConnectAsync(endpointUri, timeout.Token).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return $"Connection timed out after {timeoutMilliseconds} ms.";
        }
        catch (Exception exception)
        {
            return GetConnectionErrorMessage(exception);
        }
    }
}
