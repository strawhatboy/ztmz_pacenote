using System;
using System.Net;
using System.Net.Sockets;
using System.Timers;
using MaterialDesignThemes.Wpf;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool
{
    public struct UDPMessage
    {
        public float Time { set; get; }
        public float LapTime { set; get; }
        public float LapDistance { set; get; }
        public float CompletionRate { set; get; } // 0-1, 0.5 means finished 50%
        public float Speed { set; get; }
        public float TrackLength { set; get; }
        public float PosX { set; get; }
        public float PosY { set; get; }
        public float PosZ { set; get; }
        
        public float SpeedX { set; get; }
        public float SpeedY { set; get; }
        public float SpeedZ { set; get; }
        
        public float RollX { set; get; }
        public float RollY { set; get; }
        public float RollZ { set; get; }
        
        public float PitchX { set; get; }
        public float PitchY { set; get; }
        public float PitchZ { set; get; }

        // Wheel Pressure
        public float SpeedRearLeft { set; get; }
        public float SpeedRearRight { set; get; }
        public float SpeedFrontLeft { set; get; }

        public float SpeedFrontRight { set; get; }
        
        // pedals (0-1)
        public float Clutch { set; get; } = 1f;
        public float Brake { set; get; }
        public float Throttle { set; get; }
        
        public float Steering { set; get; }
        public float Gear { set; get; }
        public float MaxGears { set; get; }
        public float RPM { set; get; }
        public float MaxRPM { set; get; }
        public float IdleRPM { set; get; }
        public float G_long { set; get; }
        public float G_lat { set; get; }
        
        // brake tmp
        public float BrakeTempRearLeft { set; get; }
        public float BrakeTempRearRight { set; get; }
        public float BrakeTempFrontLeft { set; get; }
        public float BrakeTempFrontRight { set; get; }
        
        // suspension
        public float SuspensionRearLeft { set; get; }
        public float SuspensionRearRight { set; get; }
        public float SuspensionFrontLeft { set; get; }
        public float SuspensionFrontRight { set; get; }
        
        public float SuspensionSpeedRearLeft { set; get; }
        public float SuspensionSpeedRearRight { set; get; }
        public float SuspensionSpeedFrontLeft { set; get; }
        public float SuspensionSpeedFrontRight { set; get; }
        
        public float CurrentLap { set; get; }
        public float CarPos { set; get; }
        public float Sector { set; get; }
        public float Sector1Time { set; get; }
        public float Sector2Time { set; get; }
        public float LapsComplete { set; get; }
        public float TotalLaps { set; get; }
        public float LastLapTime { set; get; }
        

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
                   this.PosZ == target.PosZ;
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
        public event Action<int> onCollisionDetected;
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
                if (rawData.Length > 0)
                {
                    UDPMessage message = new UDPMessage();
                    message.TimeStamp = DateTime.Now;
                    message.Time = BitConverter.ToSingle(rawData, 0);
                    message.LapTime = BitConverter.ToSingle(rawData, 4);
                    message.LapDistance = BitConverter.ToSingle(rawData, 8);
                    message.CompletionRate = BitConverter.ToSingle(rawData, 12);
                    message.Speed = BitConverter.ToSingle(rawData, 28) * 3.6f; // m/s -> km/h
                    message.TrackLength = BitConverter.ToSingle(rawData, 244);
                    message.PosX = BitConverter.ToSingle(rawData, 16);
                    message.PosY = BitConverter.ToSingle(rawData, 20);
                    message.PosZ = BitConverter.ToSingle(rawData, 24);
                    message.SpeedX = BitConverter.ToSingle(rawData, 32);
                    message.SpeedY = BitConverter.ToSingle(rawData, 36);
                    message.SpeedZ = BitConverter.ToSingle(rawData, 40);
                    message.RollX = BitConverter.ToSingle(rawData, 44);
                    message.RollY = BitConverter.ToSingle(rawData, 48);
                    message.RollZ = BitConverter.ToSingle(rawData, 52);
                    message.PitchX = BitConverter.ToSingle(rawData, 56);
                    message.PitchY = BitConverter.ToSingle(rawData, 60);
                    message.PitchZ = BitConverter.ToSingle(rawData, 64);
                    message.CarPos = BitConverter.ToSingle(rawData, 39 << 2);
                    message.SpeedFrontLeft = BitConverter.ToSingle(rawData, 27 << 2) * 3.6f; // m/s -> km/h
                    message.SpeedFrontRight = BitConverter.ToSingle(rawData, 28 << 2) * 3.6f; // m/s -> km/h
                    message.SpeedRearLeft = BitConverter.ToSingle(rawData, 25 << 2) * 3.6f; // m/s -> km/h
                    message.SpeedRearRight = BitConverter.ToSingle(rawData, 26 << 2) * 3.6f; // m/s -> km/h

                    message.Clutch = BitConverter.ToSingle(rawData, 32 << 2);
                    message.Brake = BitConverter.ToSingle(rawData, 31 << 2);
                    message.Throttle = BitConverter.ToSingle(rawData, 29 << 2);

                    message.Steering = BitConverter.ToSingle(rawData, 30 << 2);
                    message.Gear = BitConverter.ToSingle(rawData, 33 << 2);
                    message.MaxGears = BitConverter.ToSingle(rawData, 65 << 2);
                    message.RPM = BitConverter.ToSingle(rawData, 37 << 2) * 10f;
                    message.MaxRPM = BitConverter.ToSingle(rawData, 63 << 2) * 10f;
                    message.IdleRPM = BitConverter.ToSingle(rawData, 64 << 2) * 10f;
                    message.G_lat = BitConverter.ToSingle(rawData, 34 << 2);
                    message.G_long = BitConverter.ToSingle(rawData, 35 << 2);
                    
                    message.BrakeTempRearLeft = BitConverter.ToSingle(rawData, 51 << 2);
                    message.BrakeTempRearRight = BitConverter.ToSingle(rawData, 52 << 2);
                    message.BrakeTempFrontLeft = BitConverter.ToSingle(rawData, 53 << 2);
                    message.BrakeTempFrontRight = BitConverter.ToSingle(rawData, 54 << 2);
                    
                    message.SuspensionRearLeft = BitConverter.ToSingle(rawData, 17 << 2);
                    message.SuspensionRearRight = BitConverter.ToSingle(rawData, 18 << 2);
                    message.SuspensionFrontLeft = BitConverter.ToSingle(rawData, 19 << 2);
                    message.SuspensionFrontRight = BitConverter.ToSingle(rawData, 20 << 2);
                    
                    message.SuspensionSpeedRearLeft = BitConverter.ToSingle(rawData, 21 << 2);
                    message.SuspensionSpeedRearRight = BitConverter.ToSingle(rawData, 22 << 2);
                    message.SuspensionSpeedFrontLeft = BitConverter.ToSingle(rawData, 23 << 2);
                    message.SuspensionSpeedFrontRight = BitConverter.ToSingle(rawData, 24 << 2);
                    
                    message.CurrentLap = BitConverter.ToSingle(rawData, 36 << 2);
                    message.LapsComplete = BitConverter.ToSingle(rawData, 59 << 2);
                    message.LastLapTime = BitConverter.ToSingle(rawData, 62 << 2);
                    message.TotalLaps = BitConverter.ToSingle(rawData, 60 << 2);
                    
                    message.Sector = BitConverter.ToSingle(rawData, 48 << 2);
                    message.Sector1Time = BitConverter.ToSingle(rawData, 49 << 2);
                    message.Sector2Time = BitConverter.ToSingle(rawData, 50 << 2);
                    // only 264 bytes
                    // message.TrackNumber = BitConverter.ToInt32(rawData, 272);
                    if (!message.Equals(this.LastMessage))
                    {
                        var lastMessage = this.LastMessage;
                        this.LastMessage = message;
                        var spdDiff = lastMessage.Speed - message.Speed;
                        if (Config.Instance.PlayCollisionSound && message.Speed != 0)
                        {
                            // collision happens. speed == 0 means reset or end stage
                            if (spdDiff >= Config.Instance.CollisionSpeedChangeThreshold_Severe)
                                this.onCollisionDetected?.Invoke(2);
                            else if (spdDiff >= Config.Instance.CollisionSpeedChangeThreshold_Medium)
                                this.onCollisionDetected?.Invoke(1);
                            else if (spdDiff >= Config.Instance.CollisionSpeedChangeThreshold_Slight)
                                this.onCollisionDetected?.Invoke(0);
                        }

                        if (Config.Instance.PlayWheelAbnormalSound)
                        {
                            // try to report wheel event
                            var wheelData = new float[]
                            {
                                message.SpeedFrontLeft, message.SpeedFrontRight, message.SpeedRearLeft,
                                message.SpeedRearRight
                            };
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

                            if (mean > 50 && minWheelSpd <
                                mean / (1 + Config.Instance.WheelAbnormalPercentageReportThreshold))
                            {
                                // need to report
                                WheelAbnormalDetectedCounter[minWheelSpdIndex]++;
                                if (WheelAbnormalDetectedCounter[minWheelSpdIndex] >=
                                    Config.Instance.WheelAbnormalFramesReportThreshold &&
                                    !this.WheelAbnormalDetectedReported[minWheelSpdIndex])
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
                        if (this.GameState == GameState.Racing || this.GameState == GameState.RaceBegin ||
                            this.GameState == GameState.CountDown)
                        {
                            this.GameState = GameState.Paused;
                        }
                    }
                }
            }
            catch
            {
            }

            if (this.isRunning)
            {
                u.BeginReceive(this.receiveMessage, result.AsyncState);
            }
        }
    }
}