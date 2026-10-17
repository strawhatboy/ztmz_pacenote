using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

internal sealed class ACRProtonGameDataReader : IGameDataReader, IGameLivePacenoteSource
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly object _stateLock = new();
    private CancellationTokenSource? _cancellation;
    private TcpListener? _listener;
    private Task? _listenerTask;
    private ACRLivePacenoteBridge? _livePacenoteProcessor;
    private ACRProtonEndpoint? _endpoint;
    private bool _dataAvailable;
    private ACRGameRawData _lastRawData = new();
    private ACRGameRawData _currentRawData = new();

    public GameState GameState { get; set; } = GameState.Unknown;
    public GameData LastGameData { get; set; }
    public GameData CurrentGameData { get; set; }
    public string TrackName => _currentRawData.StaticInfo.TrackName ?? string.Empty;
    public string CarName => _currentRawData.StaticInfo.VehicleModel ?? string.Empty;
    public string CarClass => GetCarClass(CarName);

    public event Action<GameData, GameData>? onNewGameData;
    public event Action<bool>? onGameDataAvailabilityChanged;
    public event Action<GameStateChangeEvent>? onGameStateChanged;
    public event Action<CarDamageEvent>? onCarDamaged;
    public event Action? onCarReset;
    public event Action<LivePacenoteEvent>? onLivePacenote;

    public bool Initialize(IGame game)
    {
        if (_cancellation != null)
        {
            return true;
        }

        try
        {
            _cancellation = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(1);
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            _endpoint = new ACRProtonEndpoint(port, token);
            ACRProtonBridgeRuntime.RegisterEndpoint(_endpoint);
            _livePacenoteProcessor = new ACRLivePacenoteBridge(
                pacenote => onLivePacenote?.Invoke(pacenote),
                PublishGameState,
                damage => onCarDamaged?.Invoke(damage),
                listenForNamedPipe: false);
            _listenerTask = Task.Run(() => ListenAsync(_listener, _endpoint, _cancellation.Token));
            Logger.Info("ACR Proton relay listener started on loopback port {0}.", port);
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Unable to start the ACR Proton relay listener.");
            Uninitialize(game);
            return false;
        }
    }

    public void Uninitialize(IGame game)
    {
        var cancellation = _cancellation;
        var listenerTask = _listenerTask;
        var endpoint = _endpoint;
        _cancellation = null;
        _listenerTask = null;
        _endpoint = null;

        cancellation?.Cancel();
        _listener?.Stop();
        _listener = null;
        if (listenerTask != null)
        {
            try
            {
                listenerTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
            {
            }
        }
        cancellation?.Dispose();
        _livePacenoteProcessor?.Dispose();
        _livePacenoteProcessor = null;
        if (endpoint != null)
        {
            ACRProtonBridgeRuntime.ClearEndpoint(endpoint);
        }
        SetDataAvailable(false);
        GameState = GameState.Unknown;
        Logger.Info("ACR Proton relay listener stopped.");
    }

    private async Task ListenAsync(
        TcpListener listener,
        ACRProtonEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                client.NoDelay = true;
                await ProcessConnectionAsync(client.GetStream(), endpoint, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, "ACR Proton relay connection failed; waiting for a replacement relay.");
            }
            finally
            {
                ACRProtonBridgeRuntime.SetConnected(false);
                SetDataAvailable(false);
            }
        }
    }

    private async Task ProcessConnectionAsync(
        NetworkStream stream,
        ACRProtonEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        var hello = await ACRRelayProtocol.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        if (hello is not { Type: ACRRelayFrameType.Hello } || !AuthenticateHello(hello.Value.Payload, endpoint.Token))
        {
            Logger.Warn("Rejected an unauthenticated ACR Proton relay connection.");
            return;
        }

        ACRProtonBridgeRuntime.SetConnected(true);
        Logger.Info("ACR Proton relay connected.");
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await ACRRelayProtocol.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                return;
            }

            switch (frame.Value.Type)
            {
                case ACRRelayFrameType.Telemetry:
                    ProcessTelemetry(frame.Value.Payload);
                    break;
                case ACRRelayFrameType.Pacenote:
                    var message = Encoding.UTF8.GetString(frame.Value.Payload);
                    if (_livePacenoteProcessor?.EnqueueMessage(message) != true)
                    {
                        Logger.Warn("Dropped an ACR Proton live-pacenote message because the processing queue is full.");
                    }
                    break;
                case ACRRelayFrameType.Status:
                    ProcessStatus(frame.Value.Payload);
                    break;
            }
        }
    }

    private static bool AuthenticateHello(byte[] payload, string expectedToken)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            return root.TryGetProperty("relayVersion", out var version) && version.GetInt32() == 1 &&
                root.TryGetProperty("token", out var token) &&
                string.Equals(token.GetString(), expectedToken, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void ProcessTelemetry(byte[] payload)
    {
        if (payload.Length != ACRRelayProtocol.TelemetrySize)
        {
            Logger.Warn("Ignoring ACR Proton telemetry frame with unexpected size {0}.", payload.Length);
            return;
        }

        var raw = new ACRGameRawData
        {
            GraphicsData = ReadStructure<ACRGraphicsData>(payload.AsSpan(0, ACRRelayProtocol.GraphicsSize)),
            PhysicsData = ReadStructure<ACRPhysicsData>(payload.AsSpan(
                ACRRelayProtocol.GraphicsSize,
                ACRRelayProtocol.PhysicsSize)),
            PhysicsDataValid = true,
            StaticInfo = ReadStructure<ACRStaticInfo>(payload.AsSpan(
                ACRRelayProtocol.GraphicsSize + ACRRelayProtocol.PhysicsSize,
                ACRRelayProtocol.StaticSize))
        };

        GameData previous;
        GameData current;
        ACRGameRawData previousRaw;
        lock (_stateLock)
        {
            previousRaw = _currentRawData;
            _lastRawData = previousRaw;
            _currentRawData = raw;
            previous = CurrentGameData;
            LastGameData = previous;
            CurrentGameData = ACRGameDataConverter.Convert(raw);
            current = CurrentGameData;
        }

        SetDataAvailable(true);
        onNewGameData?.Invoke(previous, current);
        PublishDamageChanges(previousRaw, raw);
    }

    private void ProcessStatus(byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var state = root.TryGetProperty("state", out var stateProperty)
                ? stateProperty.GetString() ?? "unknown"
                : "unknown";
            var detail = root.TryGetProperty("detail", out var detailProperty)
                ? detailProperty.GetString()
                : null;
            ACRProtonBridgeRuntime.SetRelayStatus(state, detail);
            Logger.Info("ACR Proton relay status: {0}{1}", state,
                string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({detail})");
        }
        catch (JsonException exception)
        {
            Logger.Warn(exception, "Ignoring malformed ACR Proton relay status.");
        }
    }

    private void PublishGameState(GameState state, Dictionary<string, object>? parameters)
    {
        if (GameState == state)
        {
            return;
        }

        var previous = GameState;
        GameState = state;
        onGameStateChanged?.Invoke(new GameStateChangeEvent
        {
            LastGameState = previous,
            NewGameState = state,
            Parameters = parameters ?? new Dictionary<string, object>()
        });
    }

    private void PublishDamageChanges(ACRGameRawData previous, ACRGameRawData current)
    {
        var oldDamage = previous.PhysicsData.VehicleDamage;
        var newDamage = current.PhysicsData.VehicleDamage;
        if (oldDamage == null || newDamage == null)
        {
            return;
        }

        for (var index = 0; index < Math.Min(oldDamage.Length, newDamage.Length); index++)
        {
            var increase = newDamage[index] - oldDamage[index];
            if (increase <= 0.01f)
            {
                continue;
            }
            onCarDamaged?.Invoke(new CarDamageEvent
            {
                DamageType = CarDamage.Bodywork,
                Parameters = new Dictionary<string, object>
                {
                    ["zone_index"] = index,
                    ["damage_increase"] = increase
                }
            });
        }
    }

    private void SetDataAvailable(bool available)
    {
        if (!available)
        {
            lock (_stateLock)
            {
                var current = CurrentGameData;
                current.ForceFeedback = null;
                CurrentGameData = current;
            }
        }
        if (_dataAvailable == available)
        {
            return;
        }
        _dataAvailable = available;
        onGameDataAvailabilityChanged?.Invoke(available);
    }

    private static T ReadStructure<T>(ReadOnlySpan<byte> source) where T : struct
    {
        if (Marshal.SizeOf<T>() != source.Length)
        {
            throw new InvalidDataException($"Unexpected {typeof(T).Name} size {source.Length}.");
        }

        var bytes = source.ToArray();
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    private static string GetCarClass(string carName)
    {
        var parts = carName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.LastOrDefault() ?? string.Empty;
    }
}
