using System;
using System.Net;
using System.Net.Sockets;
using System.Timers;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool
{
    public struct UDPMessage
    {
        public float Time { set; get; }
        public float LapTime { set; get; }
        public float LapDistance { set; get; }
        public float CompletionRate { set; get; }   // 0-1, 0.5 means finished 50%
        public float Speed { set; get; }
        public float TrackLength { set; get; }
        public float StartZ { set; get; }

        // Wheel Pressure
        public float SpeedRearLeft { set; get; }
        public float SpeedRearRight { set; get; }
        public float SpeedFrontLeft { set; get; }
        public float SpeedFrontRight { set; get; }
        // public int TrackNumber { set; get; }
        public DateTime TimeStamp { set; get; }


        public override bool Equals(object? obj)
        {
            if (obj == null)
            {
                return false;
            }
            var target = (UDPMessage)obj;
            return this.Time == target.Time &&
                   this.LapTime == target.LapTime &&
                   this.LapDistance == target.LapDistance &&
                   this.CompletionRate == target.CompletionRate &&
                   this.Speed == target.Speed &&
                   // this.TrackNumber == target.TrackNumber &&
                   this.TrackLength == target.TrackLength &&
                    this.StartZ == target.StartZ;
        }
    }

    public delegate void NewUDPMessageDelegate(UDPMessage msg);

    public delegate void GameStateChangedDelegate(GameState lastGameState, GameState newGameState);

    public struct UdpState
    {
        public UdpClient u;
        public IPEndPoint e;
    }
    public class UDPReceiver
    {
        private UdpClient client;
        private IPEndPoint any;
        public event NewUDPMessageDelegate onNewMessage;
        public event GameStateChangedDelegate onGameStateChanged;
        public event Action onCollisionDetected;
        public event Action<int> onWheelAbnormalDetected;
        public bool[] WheelAbnormalDetectedReported = new bool[] { false, false, false, false };
        public int[] WheelAbnormalDetectedCounter = new int[] { 0, 0, 0, 0 };

        private bool isRunning;
        private Timer _timer;
        private int _timerCount = 0;
        private GameState _gameState = GameState.Unknown;
        public event Action ListenStarted;
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

        public UDPReceiver()
        {
            initUDPClient();
        }

        public UDPMessage LastMessage { get; private set; }

        private void initUDPClient()
        {
            any = new IPEndPoint(IPAddress.Loopback, Config.Instance.UDPListenPort);
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
            };
            this._timer.Start(); 
            UdpState s;
            s.e = any;
            s.u = client;
            this.isRunning = true;
            client?.BeginReceive(this.receiveMessage, s);
            this.ListenStarted?.Invoke();
        }

        public void StartListening()
        {
            initUDPClient();
        }

        public void StopListening()
        {
            this.isRunning = false;
            client?.Close();
            client?.Dispose();
            client = null;
            this._timer?.Stop();
            this._timer?.Dispose();
            this._timer = null;
        }

        private void receiveMessage(IAsyncResult result)
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
            try
            {
                rawData = u.EndReceive(result, ref e);
            } catch (ObjectDisposedException x)
            {
                return;
            }
            if (rawData.Length > 0)
            {
                UDPMessage message = new UDPMessage();
                message.TimeStamp = DateTime.Now;
                message.Time = BitConverter.ToSingle(rawData, 0);
                message.LapTime = BitConverter.ToSingle(rawData, 4);
                message.LapDistance = BitConverter.ToSingle(rawData, 8);
                message.CompletionRate = BitConverter.ToSingle(rawData, 12);
                message.Speed = BitConverter.ToSingle(rawData, 28) * 3.6f;   // m/s -> km/h
                message.TrackLength = BitConverter.ToSingle(rawData, 244);
                message.StartZ = BitConverter.ToSingle(rawData, 24);
                message.SpeedFrontLeft = BitConverter.ToSingle(rawData, 27<<2) * 3.6f;
                message.SpeedFrontRight = BitConverter.ToSingle(rawData, 28<<2) * 3.6f;
                message.SpeedRearLeft = BitConverter.ToSingle(rawData, 25<<2) * 3.6f;
                message.SpeedRearRight = BitConverter.ToSingle(rawData, 26<<2) * 3.6f;
                // only 264 bytes
                // message.TrackNumber = BitConverter.ToInt32(rawData, 272);
                if (!message.Equals(this.LastMessage))
                {
                    var lastMessage = this.LastMessage;
                    this.LastMessage = message;
                    if (Config.Instance.PlayCollisionSound && lastMessage.Speed - message.Speed >= Config.Instance.CollisionSpeedChangeThreshold && message.Speed != 0)
                    {
                        // collision happens. speed == 0 means reset or end stage
                        this.onCollisionDetected?.Invoke();
                    }

                    if (Config.Instance.PlayWheelAbnormalSound)
                    {
                        // try to report wheel event
                        var wheelData = new float[] { message.SpeedFrontLeft, message.SpeedFrontRight, message.SpeedRearLeft, message.SpeedRearRight };
                        var minWheelSpd = float.MaxValue;
                        var minWheelSpdIndex = 0;
                        var sum = 0f;
                        for (int i = 0; i < 4; i++)
                        {
                            sum += wheelData[i];
                            if (wheelData[i] < minWheelSpd)
                            {
                                minWheelSpd = wheelData[i];
                                minWheelSpdIndex = i;
                            }
                        }
                        sum -= minWheelSpd;
                        var mean = sum / 3f;

                        if (minWheelSpd < mean / (1 + Config.Instance.WheelAbnormalPercentageReportThreshold))
                        {
                            // need to report
                            WheelAbnormalDetectedCounter[minWheelSpdIndex]++;
                            if (WheelAbnormalDetectedCounter[minWheelSpdIndex] >= Config.Instance.WheelAbnormalFramesReportThreshold && !this.WheelAbnormalDetectedReported[minWheelSpdIndex])
                            {
                                this.onWheelAbnormalDetected?.Invoke(minWheelSpdIndex);
                                this.WheelAbnormalDetectedReported[minWheelSpdIndex] = true;
                            }
                        }
                        else
                        {
                            // reset to normal
                            //WheelAbnormalDetectedCounter[minWheelSpdIndex] = 0;
                        }
                    }

                    this.onNewMessage?.Invoke(message);
                    if (message.LapTime > 0 && this.GameState != GameState.Racing)
                    {
                        this.GameState = GameState.Racing;
                    }
                    else if (message.LapTime == 0 && message.LapDistance <= 0)
                    {
                        // CountDown not works in Daily Event, only works for TimeTrial
                        //if (message.Time != this.LastMessage.Time)
                        //{
                        //    if (this.GameState != GameState.CountDown)
                        //        this.GameState = GameState.CountDown;
                        //}
                        //else 
                        if (message.Time == 0 && this.GameState != GameState.RaceEnd)
                        {
                            this.GameState = GameState.RaceEnd;
                        }
                        else if (this.GameState != GameState.RaceBegin)
                        {
                            this.GameState = GameState.RaceBegin;
                        }
                    }
                    //else if (message.LapTime == 0 && this.GameState != GameState.RaceEnd)
                    //{
                    //    this.GameState = GameState.RaceEnd;
                    //}
                }
                else
                {
                    if (this.GameState == GameState.Racing || this.GameState == GameState.RaceBegin || this.GameState == GameState.CountDown)
                    {
                        this.GameState = GameState.Paused;
                    }
                }

                if (this.isRunning)
                {
                    u.BeginReceive(this.receiveMessage, result.AsyncState);
                }
            }
        }
    }
}
