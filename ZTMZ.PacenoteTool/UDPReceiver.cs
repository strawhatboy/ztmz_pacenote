using System;
using System.Net;
using System.Net.Sockets;
using System.Timers;

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

    public delegate void GameStateChangedDelegate(GameState newGameState);

    public struct UdpState
    {
        public UdpClient u;
        public IPEndPoint e;
    }
    public class UDPReceiver
    {
        private UdpClient udpClient;
        public event NewUDPMessageDelegate onNewMessage;
        public event GameStateChangedDelegate onGameStateChanged;
        private bool isRunning;
        private Timer _timer = new Timer();
        private int _timerCount = 0;
        private GameState _gameState = GameState.Unknown;
        public GameState GameState
        {
            set
            {
                this._gameState = value;
                this.onGameStateChanged?.Invoke(this._gameState);
                this._timerCount = 0;
            }
            get => this._gameState;
        }

        public UDPReceiver()
        {
            initUDPClient();
        }

        public UDPMessage LastMessage { get; private set; }

        private void initUDPClient()
        {
            var any = new IPEndPoint(IPAddress.Loopback, 20777);
            var client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.ExclusiveAddressUse = false;
            client.Client.Bind(any);
            UdpState s;
            s.e = any;
            s.u = client;
            this.isRunning = true;
            client.BeginReceive(this.receiveMessage, s);
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
        }

        private void receiveMessage(IAsyncResult result)
        {
            if (result.AsyncState == null)
            {
                return;
            }
            UdpClient u = ((UdpState)(result.AsyncState)).u;
            IPEndPoint e = ((UdpState)(result.AsyncState)).e;
            byte[] rawData = u.EndReceive(result, ref e);
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
                // only 264 bytes
                // message.TrackNumber = BitConverter.ToInt32(rawData, 272);
                if (!message.Equals(this.LastMessage))
                {
                    var lastMessage = this.LastMessage;
                    this.LastMessage = message;
                    this.onNewMessage?.Invoke(message);
                    if (message.LapTime > 0 && this.GameState != GameState.Racing)
                    {
                        this.GameState = GameState.Racing;
                    }
                    else if (message.LapTime == 0 && message.LapDistance < 0)
                    {
                        // CountDown not works in Daily Event, only works for TimeTrial
                        //if (message.Time != this.LastMessage.Time)
                        //{
                        //    if (this.GameState != GameState.CountDown)
                        //        this.GameState = GameState.CountDown;
                        //}
                        //else 
                        if (this.GameState != GameState.RaceBegin)
                        {
                            this.GameState = GameState.RaceBegin;
                        }
                    }
                    else if (message.LapTime == 0 && this.GameState != GameState.RaceEnd)
                    {
                        this.GameState = GameState.RaceEnd;
                    }
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
