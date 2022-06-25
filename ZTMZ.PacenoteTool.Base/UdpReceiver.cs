using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Timers;
using MaterialDesignThemes.Wpf;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool
{
    

    public delegate void NewUDPMessageDelegate(GameData msg);

    public delegate void GameStateChangedDelegate(GameState lastGameState, GameState newGameState);

    public struct UdpState
    {
        public UdpClient u;
        public IPEndPoint e;
    }

    public class UdpReceiver
    {
        private UdpClient client;
        private IPEndPoint any;
        public event Action<byte[], byte[]> onNewMessage;
        public event GameStateChangedDelegate onGameStateChanged;
        public event Action<int> onCollisionDetected;
        public event Action<int> onWheelAbnormalDetected;
        public event Action<bool> onMessageAvailable;
        public bool[] WheelAbnormalDetectedReported = new bool[] { false, false, false, false };
        public int[] WheelAbnormalDetectedCounter = new int[] { 0, 0, 0, 0 };

        private bool isRunning = false;
        private bool isInitialized = false;
        private Timer _timer;
        private int _timerCount = 0;
        private int _timerMessageAvailableCount = 0;
        private GameState _gameState = GameState.Unknown;
        public event Action ListenStarted;
        private byte[] lastMessage;

        public GameState GameState
        {
            set
            {
                var lastGameState = this._gameState;
                this._gameState = value;
                this.onGameStateChanged?.Invoke(lastGameState, this._gameState);
                this._timerCount = 0;
            }
            get => this._gameState;
        }

        public void ResetWheelStatus()
        {
            WheelAbnormalDetectedReported = new bool[] { false, false, false, false };
            WheelAbnormalDetectedCounter = new int[] { 0, 0, 0, 0 };
        }

        public UdpReceiver()
        {
            // initUDPClient();
        }

        public GameData LastMessage { get; private set; }

        private void initUDPClient(IPAddress ipAddress, int port)
        {
            any = new IPEndPoint(ipAddress, port);
            client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.ExclusiveAddressUse = false;
            client.Client.Bind(any);
            this._timer = new Timer();
            this._timer.Interval = 1000;
            this._timer.Elapsed += (sender, args) =>
            {
                if (this.GameState != GameState.Paused && this.GameState != GameState.RaceBegin)
                {
                    if (this._timerCount >= 10)
                    {
                        // no gamestate change for 20s and the game is not paused.
                        this.GameState = GameState.Unknown;
                        this._timerCount = 0;
                    }
                    else
                    {
                        this._timerCount++;
                    }
                }

                this._timerMessageAvailableCount++;
                if (this._timerMessageAvailableCount >= 10)
                {
                    this.onMessageAvailable?.Invoke(false);
                }
                else
                {
                    this.onMessageAvailable?.Invoke(true);
                }
            };
            this._timer.Start();
            UdpState s;
            s.e = any;
            s.u = client;
            this.isRunning = true;
            client?.BeginReceive(this.receiveMessage, s);
            this.isInitialized = true;
            this.ListenStarted?.Invoke();
        }

        public void StartListening() 
        {
            StartListening(IPAddress.Loopback, Config.Instance.UDPListenPort);
        }
        public void StartListening(IPAddress ipAddress, int port)
        {
            if (!this.isInitialized) {
                initUDPClient(ipAddress, port);
            }
        }

        public void StopListening()
        {
            this.isRunning = false;
            if (this.client != null && this.client.Client != null && this.client.Client.Connected) 
            {
                this.client.Client.Disconnect(true);
                this.client.Close();
            }
            this.isInitialized = false;
        }

        private void receiveMessage(IAsyncResult result)
        {
            try
            {


                if (result.AsyncState == null && !this.isRunning)
                {
                    return;
                }

                UdpClient u = ((UdpState)(result.AsyncState)).u;
                IPEndPoint e = ((UdpState)(result.AsyncState)).e;
                byte[] rawData;
                if (u == null || u.Client == null || e == null)
                {
                    return;
                }

                rawData = u.EndReceive(result, ref e);
                if (rawData.Length > 0)
                {
                    this.onNewMessage?.Invoke(lastMessage, rawData);
                    lastMessage = rawData;
                }

                if (this.isRunning)
                {
                    u.BeginReceive(this.receiveMessage, result.AsyncState);
                }
            }
            catch (Exception ex)
            {
                if (ex is ObjectDisposedException || ex is SocketException)
                {
                    Debug.WriteLine("Socket is closed");
                    return;
                }
                throw;
            }
        }
    }
}
