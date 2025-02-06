// To record video with OBS

using System;
using System.Threading.Tasks;
using OBSWebsocketDotNet;

namespace ZTMZ.PacenoteTool.Base;

public class ObsManager : IDisposable {
    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static ObsManager _instance;
    private static readonly object _lock = new object();

    private OBSWebsocket _obs;

    private ObsManager() { }

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

    public async Task<bool> Connect() {
        // Connect to OBS
        if (_obs != null && _obs.IsConnected) {
            return true;
        }

        _logger.Info("Connecting to OBS");

        _obs = new OBSWebsocket();
        var tcs = new TaskCompletionSource<bool>();
        _obs.Connected += (sender, args) => {
            // OBS connected
            _logger.Info("OBS connected!");
            tcs.SetResult(true);
        };

        try {
            _obs.ConnectAsync(Config.Instance.ReplayOBSWebsocketUrl, Config.Instance.ReplayOBSWebsocketPassword);
            bool result = await Task.WhenAny(tcs.Task, Task.Delay(Config.Instance.ReplayOBSWebsocketTimeout)) == tcs.Task;
            if (!result) {
                _logger.Error("Failed to connect to OBS because of timeout");
                return false;
            } else {
                return true;
            }
        } catch (Exception ex) {
            _logger.Error($"Failed to connect to OBS because of \n{ex.ToString()}");
            return false;
        }
    }

    public async void StartRecording() {
        _logger.Info("Start recording video with OBS");
        // Start recording video
        if (await Connect()) {
            _logger.Info("OBS connected. start recording video with OBS");
            try {
                _obs.StartRecord();
            } catch (Exception ex) {
                _logger.Error($"Failed to start recording video with OBS because of \n{ex.ToString()}");
            }
        } else {
            _logger.Error("Failed to connect to OBS");
        }
    }

    public string StopRecording() {
        _logger.Info("Stop recording video with OBS");
        if (_obs != null && _obs.IsConnected) {
            _logger.Info("OBS connected. stop recording video with OBS");
            try {
                var output_path =  _obs.StopRecord();
                _logger.Info($"Video saved at {output_path}");
                return output_path;
            } catch (Exception ex) {
                _logger.Error($"Failed to stop recording video with OBS because of \n{ex.ToString()}");
                return "";
            }
        } else {
            _logger.Error("OBS not connected!");
            return "";
        }
    }

    public void Dispose()
    {
        _obs?.Disconnect();
    }
}
