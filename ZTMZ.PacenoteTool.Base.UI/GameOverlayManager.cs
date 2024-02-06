
#define DEV
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Xps.Serialization;
using GameOverlay.Windows;
using GameOverlay.Drawing;
using SharpDX;
using SharpDX.Direct2D1;
using ZTMZ.PacenoteTool.Base;
using Geometry = GameOverlay.Drawing.Geometry;
using Image = GameOverlay.Drawing.Image;
using Point = GameOverlay.Drawing.Point;
using Rectangle = GameOverlay.Drawing.Rectangle;
using Color = GameOverlay.Drawing.Color;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base.UI;
using System.IO;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base.UI
{

    public class GameOverlayManager
    {
        private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
#if DEV
        public static string GAME_PROCESS = "notepad";
        public static string GAME_WIN_TITLE = "Dirt Rally 2.0";
#else
        public static string GAME_PROCESS = "dirtrally2";
        public static string GAME_WIN_TITLE = "Dirt Rally 2.0";
#endif
        private StickyWindow _window;

        private readonly Dictionary<string, SolidBrush> _brushes = new();
        private readonly Dictionary<string, Font> _fonts = new();
        private readonly Dictionary<string, Image> _images = new();
        private BackgroundWorker _bgw;
        private bool _isRunning;
        

        private Random _random;

        public DashboardScriptArguments DashboardScriptArguments { set; get; } = new DashboardScriptArguments() {
            Config = Config.Instance,
            I18NLoader = I18NLoader.Instance,
            GameData = new GameData(),
            GameContext = new GameContext(),
            GameOverlayDrawingHelper = new GameOverlayDrawingHelper()
        };

        private bool _TimeToShowTelemetry = false;

        public bool TimeToShowTelemetry
        {
            set
            {
                _TimeToShowTelemetry = value;
                maxSpeed = MAX_SPEED;
                maxWheelSpeed = MAX_WHEEL_SPEED;
                maxWheelTemp = MAX_WHEEL_TEMP;
                maxGForce = MAX_G_FORCE;
                maxSuspension = MAX_SUSPENSION;
                minSuspension = MIN_SUSPENSION;
                maxSuspensionSpd = MAX_SUSPENSION_SPD;
                minSuspensionSpd = MIN_SUSPENSION_SPD;
            }
            get
            {
                return _TimeToShowTelemetry;
            }
        }

        public bool TimeToShowStatistics { set; get; }

        public GameData GameData { set; get; }

        public static float MAX_SPEED = 200f;
        public static float MAX_WHEEL_SPEED = 220f;
        public static float MAX_WHEEL_TEMP = 800f;
        public static float MAX_G_FORCE = 2.2f;
        public static float MAX_SUSPENSION_SPD = 1000f; // m/s
        public static float MIN_SUSPENSION_SPD = -1000f;
        public static float MAX_SUSPENSION = 200;
        public static float MIN_SUSPENSION = -200;
        
        private float maxSpeed { set; get; }
        private float maxWheelSpeed { set; get; }
        private float maxWheelTemp { set; get; }
        private float maxGForce { set; get; }
        private float maxSuspensionSpd { set; get; }
        private float minSuspensionSpd { set; get; }
        private float maxSuspension { set; get; }
        private float minSuspension { set; get; }

        public List<Dashboard> Dashboards { set; get; } = new List<Dashboard>();
        public List<bool> DashboardEnabled { set; get; } = new List<bool>();

        public List<bool> DashboardErrorEncountered { set; get; } = new List<bool>();

        private int initializeRetryCount = 0;
        private int initializeRetryMax = 30;    // 30 seconds
        private int initializeRetryInterval = 1000;

        public GameOverlayManager() {
            var dashboardsPath = AppLevelVariables.Instance.GetPath(Constants.PATH_DASHBOARDS);
            if (System.IO.Directory.Exists(dashboardsPath))
            {
                // loop through subfolders and load all dashboards
                foreach (var dashboardPath in System.IO.Directory.GetDirectories(dashboardsPath))
                {
                    if (!System.IO.File.Exists(System.IO.Path.Combine(dashboardPath, Constants.DASHBOARD_INFO_FILE_NAME)))
                    {
                        _logger.Warn("Dashboard info.json not exists: {0}", dashboardPath);
                        continue;
                    }
                    var dashboard = new Dashboard(System.IO.Path.Combine(dashboardPath, Constants.DASHBOARD_INFO_FILE_NAME));
                    // dashboard.Descriptor.Path = dashboardPath;
                    Dashboards.Add(dashboard);
                }
            }
            else
            {
                _logger.Warn("Dashboard path not exists: {0}", dashboardsPath);
            }
            _logger.Info($"Loaded {Dashboards.Count} Dashboards.");
        }

        public void InitializeOverlay(nint processWindowHandle)
        {
            _logger.Info("Initializing overlay....");
#if DEV
            DashboardScriptArguments.GameData = new GameData()
            {
                Brake = 0.5f,
                Throttle = 0.3f,
                Clutch = 0.65f,
                HandBrake = 1.0f,
                RPM = 9000f,
                MaxRPM = 9000f,
                IdleRPM = 1000f,
                Speed = 130f,
                Gear = 3f,
                MaxGears = 4,
                G_lat = 0.5f,
                G_long = 0.2f,
                SpeedFrontLeft = 190f,
                SpeedRearLeft = 28f,
                SpeedFrontRight = 181f,
                SpeedRearRight = 59f,
                BrakeTempFrontLeft = 99,
                BrakeTempFrontRight = 98,
                BrakeTempRearLeft = 90,
                BrakeTempRearRight = 91,
                SuspensionFrontLeft = 0.8f,
                SuspensionFrontRight = 0.7f,
                SuspensionRearLeft = 0.75f,
                SuspensionRearRight = 0.71f,
                CompletionRate = 0.35f,
                Steering = 0.9f,
                SuspensionSpeedFrontLeft = 20,
                SuspensionSpeedFrontRight = 23,
                SuspensionSpeedRearLeft = 35,
                SuspensionSpeedRearRight = 46,
                Time = 20,
                CarPos = 30,
                LapDistance = 200,
                LapTime = 20,
                PosX = 245,
                PosY = 425,
                PosZ = 5356,
                TimeStamp = DateTime.Now,
                TrackLength = 35667,
            };
            TimeToShowTelemetry = true;
#endif
            var gfx = new Graphics()
            {
                MeasureFPS = true,
                PerPrimitiveAntiAliasing = true,
                TextAntiAliasing = true
            };

            // set graphics to dashboard arguments
            DashboardScriptArguments.Graphics = gfx;

            Task.Run(() => {
                // retry if failed
                // possible crash because of window not responding
                while (initializeRetryCount < initializeRetryMax)
                {
                    try
                    {
                        _window = new StickyWindow(processWindowHandle, gfx);
                        initializeRetryCount = 0;
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to initialize overlay, retrying...");
                        initializeRetryCount++;
                        Thread.Sleep(initializeRetryInterval);
                    }
                }

                _window.Title = Constants.HUD_WINDOW_NAME;
                if (Config.Instance.HudLockFPS) {
                    _window.FPS = Config.Instance.HudFPS;   // 60 fps by default
                } else {
                    _window.FPS = 0;    // no limit
                }
                _window.AttachToClientArea = true;
                if (Config.Instance.HudTopMost) 
                {
                    _window.IsTopmost = Config.Instance.HudTopMost;
                } else 
                {
                    _window.BypassTopmost = true;
                }
                _window.IsVisible = true;


                _window.DestroyGraphics += _window_DestroyGraphics;
                _window.DrawGraphics += _window_DrawGraphics;
                _window.SetupGraphics += _window_SetupGraphics;

                this.Run();
                _logger.Info("GameOverlay initialized.!");
            });
        }

        public void UninitializeOverlay()
        {
            _isRunning = false;
            _window?.Dispose();
        }

        public void SetFPS(int fps)
        {
            if (_window != null)
                _window.FPS = fps;  
        }

        private void _window_SetupGraphics(object sender, SetupGraphicsEventArgs e) {
            var gfx = e.Graphics;
            // load custom fonts
            gfx.LoadCustomFont(AppLevelVariables.Instance.GetPath(Constants.PATH_FONTS));
            _brushes["clear"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
            foreach (var dashboard in Dashboards) {
                DashboardErrorEncountered.Add(false);
                if (dashboard.Descriptor.IsEnabled) {
                    dashboard.Load(DashboardScriptArguments);
                    DashboardEnabled.Add(true);
                } else {
                    DashboardEnabled.Add(false);
                }
            }
        }

        private void _window_DrawGraphics(object sender, DrawGraphicsEventArgs e) {
            var gfx = e.Graphics;
            gfx.ClearScene(_brushes["clear"]);
            if (TimeToShowTelemetry) {
                for (var i = 0; i < Dashboards.Count; i++) {
                    var dashboard = Dashboards[i];
                    if (dashboard.Descriptor.IsEnabled) {
                        if (DashboardEnabled[i]) {
                            try {
                                dashboard.Render(DashboardScriptArguments);
                            } catch (Exception ex) {
                                // log error for the first time for this dashboard
                                if (!DashboardErrorEncountered[i]) {
                                    _logger.Error(ex, "Error when rendering dashboard: {0}", dashboard.Descriptor.Name);
                                    DashboardErrorEncountered[i] = true;
                                }
                            }
                        } else {
                            dashboard.Load(DashboardScriptArguments);
                            DashboardEnabled[i] = true;
                        }
                    } else {
                        if (DashboardEnabled[i]) {
                            dashboard.Unload();
                            DashboardEnabled[i] = false;
                        }
                    }
                }
            }
        }

        // private void _window_SetupGraphics(object sender, SetupGraphicsEventArgs e)
        // {
        //     var gfx = e.Graphics;

        //     if (e.RecreateResources)
        //     {
        //         foreach (var pair in _brushes) pair.Value.Dispose();
        //         foreach (var pair in _images) pair.Value.Dispose();
        //     }

        //     _brushes["black"] = gfx.CreateSolidBrush(0, 0, 0);
        //     _brushes["white"] = gfx.CreateSolidBrush(255, 255, 255);
        //     _brushes["red"] = gfx.CreateSolidBrush(255, 0, 0);
        //     _brushes["grey"] = gfx.CreateSolidBrush(64, 64, 64);
        // if (Config.Instance.HudChromaKeyMode)
        //     {
        //         // change green to blue, blue to purple, clear to green
        //         _brushes["green"] = gfx.CreateSolidBrush(0, 0, 255);
        //         _brushes["blue"] = gfx.CreateSolidBrush(255, 0, 255);
        //         _brushes["clear"] = gfx.CreateSolidBrush(0, 255, 0);
        //     }
        //     else
        //     {
        //         _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
        //         _brushes["blue"] = gfx.CreateSolidBrush(0, 0, 255);
        //         _brushes["clear"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
        //     }
        //     _brushes["background"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 100);
            

        //     _brushes["grid"] = gfx.CreateSolidBrush(255, 255, 255, 0.2f);
        //     _brushes["random"] = gfx.CreateSolidBrush(0, 0, 0);
            
        //     _brushes["telemetryBackground"] = gfx.CreateSolidBrush(0x3, 0x6, 0xF, 255);

        //     if (e.RecreateResources) return;

        //     _fonts["arial"] = gfx.CreateFont("Arial", 12);
        //     _fonts["consolas"] = gfx.CreateFont("Consolas", 14);

        //     _fonts["telemetryGear"] = gfx.CreateFont("Consolas", 32);
        // }

        private void _window_DestroyGraphics(object sender, DestroyGraphicsEventArgs e)
        {
            foreach (var pair in _brushes) pair.Value.Dispose();
            foreach (var pair in _fonts) pair.Value.Dispose();
            foreach (var pair in _images) pair.Value.Dispose();
            foreach (var dashboard in Dashboards) {
                if (dashboard.Descriptor.IsEnabled)
                    dashboard.Unload();
            }
        }

        // private void _window_DrawGraphics(object sender, DrawGraphicsEventArgs e)
        // {
        //     var gfx = e.Graphics;
        //     gfx.ClearScene(_brushes["clear"]);
        //     //_gridBounds = new Rectangle(20, 60, gfx.Width - 600, gfx.Height - 20);

        //     try
        //     {
        //         if (gfx.Height == 0 || gfx.Width == 0) 
        //         {
        //             // the window is not yet visible
        //             return;
        //         }

        //         drawBasicInfo(gfx);
        //         if (Config.Instance.HudShowTelemetry && TimeToShowTelemetry)
        //         {
        //             drawTelemetry(gfx);
        //         }

        //         if (Config.Instance.HudShowDebugTelemetry && TimeToShowTelemetry)
        //         {
        //             drawDebugTelemetry(gfx);
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         _logger.Error("We got exception when drawing hud: {0}", ex.ToString());
        //         GoogleAnalyticsHelper.Instance.TrackExceptionEvent("We got exception when drawing hud", ex.ToString());
        //     }
        // }

        private void drawBasicInfo(Graphics gfx)
        {
            var padding = 200;
            // var infoText = new StringBuilder()
            //     .Append(I18NLoader.Instance["overlay.track"].PadRight(16)).Append(GameContext.TrackName.PadRight(padding)).AppendLine()
            //     .Append(I18NLoader.Instance["overlay.audioPackage"].PadRight(16)).Append(GameContext.AudioPackage.PadRight(padding)).AppendLine()
            //     .Append(I18NLoader.Instance["overlay.scriptAuthor"].PadRight(16)).Append(GameContext.ScriptAuthor.PadRight(padding)).AppendLine()
            //     .Append(I18NLoader.Instance["overlay.dyanmic"].PadRight(16)).Append(GameContext.PacenoteType.PadRight(padding))
            //     .ToString();
            // var size = gfx.MeasureString(_fonts["consolas"], _fonts["consolas"].FontSize, infoText);
            // gfx.DrawTextWithBackground(_fonts["consolas"], _brushes["green"], _brushes["background"], gfx.Width - size.X, 0, infoText);
        }

        private void drawDebugTelemetry(Graphics gfx)
        {
            // gfx.DrawTextWithBackground(_fonts["consolas"], _brushes["green"], _brushes["background"], 0, 0, GameData.ToString());
        }

        private void drawTelemetry(Graphics gfx)
        {
            List<Action<Graphics, float, float, float, float>> drawFuncs = new();
            if (Config.Instance.HudTelemetryShowGBall) drawFuncs.Add(drawGBall);
            if (Config.Instance.HudTelemetryShowSpdSector) drawFuncs.Add(drawSpdSector);
            if (Config.Instance.HudTelemetryShowRPMSector) drawFuncs.Add(drawRPMSector);
            if (Config.Instance.HudTelemetryShowPedals) drawFuncs.Add(drawPedals);
            if (Config.Instance.HudTelemetryShowGear) drawFuncs.Add(drawGear);
            if (Config.Instance.HudTelemetryShowSteering) drawFuncs.Add(drawSteering);
            if (Config.Instance.HudTelemetryShowSuspensionBars) drawFuncs.Add(drawSuspensionBars);
            
            // calculate the margin, padding, pos of each element
            var telemetryHeight = gfx.Height * Config.Instance.HudSizePercentage;
            var telemetryWidth = telemetryHeight * drawFuncs.Count; // elements are squre?
            var telemetryStartPosX = 0.5f * (gfx.Width - telemetryWidth);
            var telemetryStartPosY = gfx.Height - telemetryHeight;

            var telemetryPaddingH = telemetryHeight * Config.Instance.HudPaddingH;
            var telemetryPaddingV = telemetryHeight * Config.Instance.HudPaddingV;

            var telemetrySpacing = telemetryHeight * Config.Instance.HudElementSpacing;
            
            // drawBackground
            _brushes["telemetryBackground"].Color = new Color(
                _brushes["telemetryBackground"].Color.R,
                _brushes["telemetryBackground"].Color.G,
                _brushes["telemetryBackground"].Color.B,
                255 * Config.Instance.HudBackgroundOpacity);
            gfx.FillRectangle(_brushes["telemetryBackground"], 
                telemetryStartPosX,
                telemetryStartPosY,
                telemetryStartPosX + telemetryWidth,
                telemetryStartPosY + telemetryHeight);

            var elementStartX = telemetryStartPosX + telemetryPaddingH;
            var elementStartY = telemetryStartPosY + telemetryPaddingV;
            var elementHeight = telemetryHeight - telemetryPaddingV * 2f;
            var elementWidth = ((telemetryWidth - telemetryPaddingH * 2f) - (drawFuncs.Count-1) * telemetrySpacing) /
                               drawFuncs.Count;

            foreach (var t in drawFuncs)
            {

                try
                {
                    //TODO: suppress unknown ex for now, I have no env for testing...
                    t(gfx, elementStartX, elementStartY, elementWidth, elementHeight);
                }
                catch (Exception ex)
                {
                    _logger.Error("We got exception when drawing elements: {0}, {1}", ex.ToString(), GameData.ToString());
                    // GoogleAnalyticsHelper.Instance.TrackExceptionEvent($"We got exception when drawing elements with func: {t.ToString()}", ex.Message + UdpMessage.ToString());
                }

                elementStartX += elementWidth + telemetrySpacing;
            }
            
            // draw the finish rate
            gfx.FillRectangle(_brushes["green"], 
                0, 
                telemetryStartPosY + telemetryHeight - 5, 
                GameData.CompletionRate * gfx.Width,
                telemetryStartPosY + telemetryHeight);
        }

        private void drawGBall(Graphics gfx, float x, float y, float width, float height)
        {
            var centerX = x + 0.5f * width;
            var centerY = y + 0.5f * height;
            var radius = MathF.Min(width, height) * 0.5f;
            gfx.FillCircle(_brushes["grey"], centerX, centerY, radius);
            gfx.DrawLine(_brushes["grid"], centerX - radius, centerY, centerX + radius, centerY, 1);
            gfx.DrawLine(_brushes["grid"], centerX, centerY - radius, centerX, centerY + radius, 1);
            gfx.DrawCircle(_brushes["white"], centerX, centerY, radius -1 , 1);
            gfx.DrawCircle(_brushes["black"], centerX, centerY, radius , 1);
            // the ball
            var ballX = centerX + GameData.G_lat * radius / MAX_G_FORCE;
            var ballY = centerY + GameData.G_long * radius / MAX_G_FORCE;
            gfx.FillCircle(_brushes["red"], ballX, ballY, radius * 0.1f);
        }
        private void drawSpdSector(Graphics gfx, float x, float y, float width, float height)
        {
            maxSpeed = MathF.Max(maxSpeed, GameData.Speed); 
            drawSector(gfx, "SPD (KM/h)", x, y, width, height, GameData.Speed, maxSpeed, Config.Instance.HudSectorThicknessRatio);
        }
        private void drawPedals(Graphics gfx, float x, float y, float width, float height)
        {
            // 3 pedals
            var pedalWidth = 1f / 3.6f * width;
            var spacing = 0.3f / 3.6f * width;
            gfx.FillRectangle(_brushes["grey"], x, y, x + pedalWidth, y + height);
            gfx.FillRectangle(_brushes["grey"], x + pedalWidth + spacing, y, x + 2 * pedalWidth + spacing, y + height);
            gfx.FillRectangle(_brushes["grey"], x + 2 * pedalWidth + 2 * spacing, y, x + width, y + height);
            gfx.DrawRectangle(_brushes["white"], x, y, x + pedalWidth, y + height, 1);
            gfx.DrawRectangle(_brushes["white"], x + pedalWidth + spacing, y, x + 2 * pedalWidth + spacing, y + height, 1);
            gfx.DrawRectangle(_brushes["white"], x + 2 * pedalWidth + 2 * spacing, y, x + width, y + height, 1);
            gfx.DrawRectangle(_brushes["black"], x-1, y-1, x + pedalWidth+1, y + height+1, 1);
            gfx.DrawRectangle(_brushes["black"], x + pedalWidth + spacing-1, y-1, x + 2 * pedalWidth + spacing+1, y + height+1, 1);
            gfx.DrawRectangle(_brushes["black"], x + 2 * pedalWidth + 2 * spacing-1, y-1, x + width+1, y + height+1, 1);

            gfx.FillRectangle(_brushes["blue"], 1 + x, 1 + y + height * (1-GameData.Clutch), x + pedalWidth - 1, y + height - 1);
            gfx.FillRectangle(_brushes["red"], 1 + x + pedalWidth + spacing, 1 + y + height * (1-GameData.Brake), x + 2 * pedalWidth + spacing - 1, y + height - 1);
            gfx.FillRectangle(_brushes["green"], 1 + x + 2 * pedalWidth + 2 * spacing, 1 + y + height * (1-GameData.Throttle), x + width - 1, y + height - 1);

        }
        private void drawGear(Graphics gfx, float x, float y, float width, float height)
        {
            // var font = gfx.CreateFont("consolas", width);
            // var actualSize = MathF.Min(width, height);
            // gfx.DrawText(_fonts["consolas"], actualSize, _brushes["white"], x, y, getGearText(Convert.ToInt32(UdpMessage.Gear)));
            var columns = Convert.ToInt32(MathF.Ceiling(GameData.MaxGears * 0.5f)) + 1;

            var barWidth = width / (columns + (columns-1) * Config.Instance.HudSectorThicknessRatio);
            var spacingH = Config.Instance.HudSectorThicknessRatio * barWidth;
            var barHeight = height / (2 + Config.Instance.HudSectorThicknessRatio);
            var spacingV = barHeight * Config.Instance.HudSectorThicknessRatio;

            List<Rectangle> rectangles = new()
            {
                // R
                new Rectangle(x, y, x + barWidth, y + barHeight),
            };

            for (var i = 1; i <= GameData.MaxGears; i++)
            {
                var row = (i + 1) % 2;
                var column = (i + 1) / 2;
                rectangles.Add(new Rectangle(
                    x + column * (spacingH + barWidth),
                    y + row * (spacingV + barHeight),
                    x + barWidth + column * (spacingH + barWidth),
                    y + barHeight + row * (spacingV + barHeight)
                    ));
            }

            foreach (var r in rectangles)
            {
                gfx.FillRectangle(_brushes["white"], r);
                gfx.DrawRectangle(_brushes["black"], r, 1);
            }

            int gear = Convert.ToInt32(GameData.Gear);
            string gearText = "";
            bool isNGear = false;
            Rectangle rect;
            switch (gear) 
            {
                case -1:
                case 10:
                    rect = rectangles[0];
                    gearText = "R";
                    break;
                case 0:
                    rect = rectangles[0];
                    isNGear = true;
                    gearText = "N";
                    break;
                default:
                    rect = rectangles[gear];
                    gearText = gear.ToString();
                    break;
            }
            if (!isNGear)
            {
                gfx.FillRectangle(_brushes["red"], rect.Left + 1, rect.Top + 1, rect.Right - 1, rect.Bottom - 1);
            }
            gfx.drawTextWithBackgroundCentered(_fonts["consolas"],
                barWidth * 1.5f,
                _brushes["white"],
                _brushes["black"],
                x + 0.5f * barWidth,
                y + spacingV + 1.5f * barHeight,
                gearText);
        }

        private void drawSteering(Graphics gfx, float x, float y, float width, float height)
        {
            var centerX = x + 0.5f * width;
            var centerY = y + 0.5f * height;
            var radiusOuter = MathF.Min(width, height) * 0.5f;
            var radiusInner = radiusOuter * (1 - Config.Instance.HudSectorThicknessRatio);
            var radiusWidth = radiusOuter - radiusInner;

            var rawSteeringAngle = GameData.Steering * Config.Instance.HudTelemetrySteeringDegree * 0.5f;
            // bg
            IBrush pathBrush;
            IBrush bgBrush;
            if (MathF.Abs(rawSteeringAngle) >= 360)
            {
                pathBrush = _brushes["blue"];
                bgBrush = _brushes["white"];
            }
            else
            {
                pathBrush = _brushes["white"];
                bgBrush = _brushes["grey"];
            }
            gfx.FillCircle(bgBrush, centerX, centerY, radiusOuter);
            
            
            var steeringAngle = 90 - rawSteeringAngle;
            steeringAngle = steeringAngle / 180 * MathF.PI; // to radian
            var middle = 0.5f * (radiusInner + radiusOuter);

            
            
            gfx.FillCircle(_brushes["black"], centerX, centerY, radiusInner);
            gfx.DrawCircle(_brushes["black"], centerX, centerY, radiusOuter, 1);

            var anchorLeft = new Point(centerX + middle * MathF.Cos(steeringAngle + MathF.PI * 0.5f),
                centerY - middle * MathF.Sin(steeringAngle + MathF.PI * 0.5f));
            var anchorRight = new Point(centerX + middle * MathF.Cos(steeringAngle - MathF.PI * 0.5f),
                centerY - middle * MathF.Sin(steeringAngle - MathF.PI * 0.5f));
            var anchorBottom = new Point(centerX + middle * MathF.Cos(steeringAngle + MathF.PI),
                centerY - middle * MathF.Sin(steeringAngle + MathF.PI));
            
            // cross
            gfx.DrawLine(bgBrush, anchorLeft, anchorRight, radiusWidth);
            gfx.DrawLine(bgBrush, centerX, centerY, anchorBottom.X, anchorBottom.Y, radiusWidth);

            // cursor
            var alpha = MathF.PI / 30f;
            radiusOuter -= 1;
            
            // path
            var arcSize = MathF.Abs(rawSteeringAngle) % 360 >= 180 ? ArcSize.Large : ArcSize.Small;
            var sweepDirection = rawSteeringAngle > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise;
            var backsDirection = rawSteeringAngle < 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise;
            var geo_path = gfx.CreateGeometry();
            geo_path.BeginFigure(new Point(centerX,
                centerY - radiusOuter), true);
            geo_path.AddCurve(new Point(centerX + radiusOuter * MathF.Cos(steeringAngle),
                centerY - radiusOuter * MathF.Sin(steeringAngle)), radiusOuter, arcSize, sweepDirection);
            geo_path.AddPoint(new Point(centerX + radiusInner * MathF.Cos(steeringAngle),
                centerY - radiusInner * MathF.Sin(steeringAngle)));
            geo_path.AddCurve(new Point(centerX,
                centerY - radiusInner), radiusInner, arcSize, backsDirection);
            geo_path.EndFigure();
            geo_path.Close();

            gfx.FillGeometry(geo_path, pathBrush);
            
            var geo_cur = gfx.CreateGeometry();
            geo_cur.BeginFigure(new Point(centerX + radiusOuter * MathF.Cos(steeringAngle + alpha),
                centerY - radiusOuter * MathF.Sin(steeringAngle + alpha)), true);
            geo_cur.AddCurve(new Point(centerX + radiusOuter * MathF.Cos(steeringAngle - alpha),
                centerY - radiusOuter * MathF.Sin(steeringAngle - alpha)), radiusOuter, ArcSize.Small, SweepDirection.Clockwise);
            geo_cur.AddPoint(new Point(centerX + radiusInner * MathF.Cos(steeringAngle - alpha),
                centerY - radiusInner * MathF.Sin(steeringAngle - alpha)));
            geo_cur.AddCurve(new Point(centerX + radiusInner * MathF.Cos(steeringAngle + alpha),
                centerY - radiusInner * MathF.Sin(steeringAngle + alpha)), radiusInner, ArcSize.Small, SweepDirection.CounterClockwise);
            geo_cur.EndFigure();
            geo_cur.Close();
            
            gfx.FillGeometry(geo_cur, _brushes["red"]);
        }
        
        private string getGearText(int g)
        {
            return g switch
            {
                -1 => "R",
                10 => "R",
                0 => "N",
                _ => g.ToString()
            };
        }
        private void drawRPMSector(Graphics gfx, float x, float y, float width, float height)
        {
            var t = drawSector(gfx, "RPM", x, y, width, height, GameData.RPM, GameData.MaxRPM, Config.Instance.HudSectorThicknessRatio);

        }
        private void drawSuspensionBars(Graphics gfx, float x, float y, float width, float height)
        {
            // wheel spd, suspension, wheel temp
            var bgWidth = 0.3f * width;
            var bgHeight = 0.45f * height;
            var spacingH = 0.4f * width;
            var spacingV = 0.1f * height;
            var barWidth = bgWidth / 4f;

            // background
            gfx.FillRectangle(_brushes["grey"], x, y, x + bgWidth, y + bgHeight);
            gfx.FillRectangle(_brushes["grey"], x + bgWidth + spacingH, y, x + width, y + bgHeight);
            gfx.FillRectangle(_brushes["grey"], x, y + bgHeight + spacingV, x + bgWidth, y + height);
            gfx.FillRectangle(_brushes["grey"], x + bgWidth + spacingH, y + bgHeight + spacingV, x + width, y + height);
            // gfx.DrawRectangle(_brushes["white"], x-1, y-1, x + width+1, y + height+1, 1);
            gfx.DrawRectangle(_brushes["black"], x-1, y-1, x + width+1, y + height+1, 1);
            // gfx.DrawRectangle(_brushes["black"], x + bgWidth + spacingH, y, x + width, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["black"], x, y + bgHeight + spacingV, x + bgWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["black"], x + bgWidth + spacingH, y + bgHeight + spacingV, x + width, y + height, 1);
            
            
            // wheel spd
            // gfx.DrawRectangle(_brushes["green"], x, y + bgHeight, x + barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["green"], x + width - barWidth, y + bgHeight, x + width, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["green"], x, y + bgHeight + spacingV + bgHeight, x + barWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["green"], x + width - barWidth, y + bgHeight + spacingV + bgHeight, x + width, y + height, 1);
            // x += 1;
            // y += 1;
            // width -= 2;
            // height -= 2;
            maxWheelSpeed = MathF.Max(maxWheelSpeed, GameData.SpeedFrontLeft);
            gfx.FillRectangle(_brushes["green"], x, y + (1-GameData.SpeedFrontLeft / maxWheelSpeed) * bgHeight, x + barWidth, y + bgHeight);
            maxWheelSpeed = MathF.Max(maxWheelSpeed, GameData.SpeedFrontRight);
            gfx.FillRectangle(_brushes["green"], x + width - barWidth, y + (1-GameData.SpeedFrontRight / maxWheelSpeed) * bgHeight, x + width, y + bgHeight);
            maxWheelSpeed = MathF.Max(maxWheelSpeed, GameData.SpeedRearLeft);
            gfx.FillRectangle(_brushes["green"], x, y + bgHeight + spacingV + (1-GameData.SpeedRearLeft / maxWheelSpeed) * bgHeight, x + barWidth, y + height);
            maxWheelSpeed = MathF.Max(maxWheelSpeed, GameData.SpeedRearRight);
            gfx.FillRectangle(_brushes["green"], x + width - barWidth, y + bgHeight + spacingV + (1-GameData.SpeedRearRight / maxWheelSpeed) * bgHeight, x + width, y + height);
            
            // brake temp
            // gfx.DrawRectangle(_brushes["red"], x + barWidth, y + bgHeight, x + 2 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["red"], x + width - 2 * barWidth, y + bgHeight, x + width - barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["red"], x + barWidth, y + bgHeight + spacingV + bgHeight, x + 2 * barWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["red"], x + width - 2 * barWidth, y + bgHeight + spacingV + bgHeight, x + width - barWidth, y + height, 1);
            maxWheelTemp = MathF.Max(maxWheelTemp, GameData.BrakeTempFrontLeft);
            gfx.FillRectangle(_brushes["red"], x + barWidth, y + (1-getMaxMinBarPartition(maxWheelTemp, 0, GameData.BrakeTempFrontLeft)) * bgHeight, x + 2 * barWidth, y + bgHeight);
            maxWheelTemp = MathF.Max(maxWheelTemp, GameData.BrakeTempFrontRight);
            gfx.FillRectangle(_brushes["red"], x + width - 2 * barWidth, y + (1-getMaxMinBarPartition(maxWheelTemp, 0, GameData.BrakeTempFrontRight)) * bgHeight, x + width - barWidth, y + bgHeight);
            maxWheelTemp = MathF.Max(maxWheelTemp, GameData.BrakeTempRearLeft);
            gfx.FillRectangle(_brushes["red"], x + barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxWheelTemp, 0, GameData.BrakeTempRearLeft)) * bgHeight, x + 2 * barWidth, y + height);
            maxWheelTemp = MathF.Max(maxWheelTemp, GameData.BrakeTempRearRight);
            gfx.FillRectangle(_brushes["red"], x + width - 2 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxWheelTemp, 0, GameData.BrakeTempRearRight)) * bgHeight, x + width - barWidth, y + height);

            // suspension
            // gfx.DrawRectangle(_brushes["white"], x + 2 * barWidth, y + bgHeight, x + 3 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["white"], x + width - 3 * barWidth, y + bgHeight, x + width - 2 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["white"], x + 2 * barWidth, y + bgHeight + spacingV + bgHeight, x + 3 * barWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["white"], x + width - 3 * barWidth, y + bgHeight + spacingV + bgHeight, x + width - 2 * barWidth, y + height, 1);
            minSuspension = MathF.Min(minSuspension, GameData.SuspensionFrontLeft);
            maxSuspension = MathF.Max(maxSuspension, GameData.SuspensionFrontLeft);
            gfx.FillRectangle(_brushes["white"], x + 2 * barWidth, y + (1-getMaxMinBarPartition(maxSuspension, minSuspension, GameData.SuspensionFrontLeft)) * bgHeight, x + 3 * barWidth, y + bgHeight);
            minSuspension = MathF.Min(minSuspension, GameData.SuspensionFrontRight);
            maxSuspension = MathF.Max(maxSuspension, GameData.SuspensionFrontRight);
            gfx.FillRectangle(_brushes["white"], x + width - 3 * barWidth, y + (1-getMaxMinBarPartition(maxSuspension, minSuspension, GameData.SuspensionFrontRight)) * bgHeight, x + width - 2 * barWidth, y + bgHeight);
            minSuspension = MathF.Min(minSuspension, GameData.SuspensionRearLeft);
            maxSuspension = MathF.Max(maxSuspension, GameData.SuspensionRearLeft);
            gfx.FillRectangle(_brushes["white"], x + 2 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxSuspension, minSuspension, GameData.SuspensionRearLeft)) * bgHeight, x + 3 * barWidth, y + height);
            minSuspension = MathF.Min(minSuspension, GameData.SuspensionRearRight);
            maxSuspension = MathF.Max(maxSuspension, GameData.SuspensionRearRight);
            gfx.FillRectangle(_brushes["white"], x + width - 3 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxSuspension, minSuspension, GameData.SuspensionRearRight)) * bgHeight, x + width - 2 * barWidth, y + height);
            
            // suspension_speed
            // gfx.DrawRectangle(_brushes["blue"], x + 3 * barWidth, y + bgHeight, x + 4 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["blue"], x + width - 4 * barWidth, y + bgHeight, x + width - 3 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["blue"], x + 3 * barWidth, y + bgHeight + spacingV + bgHeight, x + 4 * barWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["blue"], x + width - 4 * barWidth, y + bgHeight + spacingV + bgHeight, x + width - 3 * barWidth, y + height, 1);
            maxSuspensionSpd = MathF.Max(maxSuspensionSpd, GameData.SuspensionSpeedFrontLeft);
            minSuspensionSpd = MathF.Min(minSuspensionSpd, GameData.SuspensionSpeedFrontLeft);
            gfx.FillRectangle(_brushes["blue"], x + 3 * barWidth, y + (1-getMaxMinBarPartition(maxSuspensionSpd, minSuspensionSpd, GameData.SuspensionSpeedFrontLeft)) * bgHeight, x + 4 * barWidth, y + bgHeight);
            maxSuspensionSpd = MathF.Max(maxSuspensionSpd, GameData.SuspensionSpeedFrontRight);
            minSuspensionSpd = MathF.Min(minSuspensionSpd, GameData.SuspensionSpeedFrontRight);
            gfx.FillRectangle(_brushes["blue"], x + width - 4 * barWidth, y + (1-getMaxMinBarPartition(maxSuspensionSpd, minSuspensionSpd, GameData.SuspensionSpeedFrontRight)) * bgHeight, x + width - 3 * barWidth, y + bgHeight);
            maxSuspensionSpd = MathF.Max(maxSuspensionSpd, GameData.SuspensionSpeedRearLeft);
            minSuspensionSpd = MathF.Min(minSuspensionSpd, GameData.SuspensionSpeedRearLeft);
            gfx.FillRectangle(_brushes["blue"], x + 3 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxSuspensionSpd, minSuspensionSpd, GameData.SuspensionSpeedRearLeft)) * bgHeight, x + 4 * barWidth, y + height);
            maxSuspensionSpd = MathF.Max(maxSuspensionSpd, GameData.SuspensionSpeedRearRight);
            minSuspensionSpd = MathF.Min(minSuspensionSpd, GameData.SuspensionSpeedRearRight);
            gfx.FillRectangle(_brushes["blue"], x + width - 4 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxSuspensionSpd, minSuspensionSpd, GameData.SuspensionSpeedRearRight)) * bgHeight, x + width - 3 * barWidth, y + height);

        }

        private float getMaxMinBarPartition(float max, float min, float value)
        {
            value = value < min ? min : value;
            value = value > max ? max : value;
            return (value - min) / (max - min);
        }

        private Point drawSector(Graphics gfx, 
            string unit,
            float x, 
            float y, 
            float width, 
            float height, 
            float value, 
            float maxValue, 
            float thicknessRatio=0.2f)
        {
            if (value > maxValue)
            {
                value = maxValue;
            }
            if (value < 0)
            {
                value = 0;
            }
            var centerX = x + 0.5f * width;
            var centerY = y + 0.5f * height;
            var radiusOuter = MathF.Min(width, height) * 0.5f;
            var radiusInner = radiusOuter * (1 - thicknessRatio);
            // draw backgroud geometry
            var geo_bg = gfx.CreateGeometry();
            geo_bg.BeginFigure(
                new Point(
                    centerX + radiusOuter * MathF.Cos(MathF.PI * 1.25f), 
                    centerY - radiusOuter * MathF.Sin(MathF.PI * 1.25f)
                    ),
                true
                );
            geo_bg.AddCurve(
                new Point(
                    centerX + radiusOuter * MathF.Cos(MathF.PI * 1.75f),
                    centerY - radiusOuter * MathF.Sin(MathF.PI * 1.75f)
                ),
                radiusOuter, ArcSize.Large
            );
            geo_bg.AddPoint(
                new Point(
                    centerX + radiusInner * MathF.Cos(MathF.PI * 1.75f),
                    centerY - radiusInner * MathF.Sin(MathF.PI * 1.75f)
                )
            );
            geo_bg.AddCurve(
                new Point(
                    centerX + radiusInner * MathF.Cos(MathF.PI * 1.25f),
                    centerY - radiusInner * MathF.Sin(MathF.PI * 1.25f)
                ),
                radiusInner, ArcSize.Large, SweepDirection.CounterClockwise
            );
            geo_bg.EndFigure();
            geo_bg.Close();
            gfx.FillGeometry(geo_bg, _brushes["grey"]);
            gfx.DrawGeometry(geo_bg, _brushes["black"], 1);
            
            // draw value
            var angle = MathF.PI * 1.25f - value / maxValue * MathF.PI * 1.5f;
            var arcSize = value / maxValue * 270f >= 180f ? ArcSize.Large : ArcSize.Small;
            radiusOuter -= 1;
            radiusInner += 1;
            geo_bg = gfx.CreateGeometry();
            geo_bg.BeginFigure(
                new Point(
                    centerX + radiusOuter * MathF.Cos(MathF.PI * 1.25f), 
                    centerY - radiusOuter * MathF.Sin(MathF.PI * 1.25f)
                ),
                true
            );
            geo_bg.AddCurve(
                new Point(
                    centerX + radiusOuter * MathF.Cos(angle),
                    centerY - radiusOuter * MathF.Sin(angle)
                ),
                radiusOuter, arcSize
            );
            geo_bg.AddPoint(
                new Point(
                    centerX + radiusInner * MathF.Cos(angle),
                    centerY - radiusInner * MathF.Sin(angle)
                )
            );
            var endPoint = new Point(
                centerX + radiusInner * MathF.Cos(MathF.PI * 1.25f),
                centerY - radiusInner * MathF.Sin(MathF.PI * 1.25f)
            );
            geo_bg.AddCurve(
                endPoint,
                radiusInner, arcSize, SweepDirection.CounterClockwise
            );
            geo_bg.EndFigure();
            geo_bg.Close();
            gfx.FillGeometry(geo_bg, _brushes["white"]);

            gfx.drawTextWithBackgroundCentered(_fonts["consolas"],
                0.2f * radiusOuter,
                _brushes["white"],
                _brushes["black"],
                centerX,
                centerY + radiusInner,
                unit);
            gfx.drawTextWithBackgroundCentered(_fonts["consolas"],
                0.5f * radiusOuter,
                _brushes["white"],
                _brushes["black"],
                centerX,
                centerY,
                Convert.ToInt32(value).ToString());

            return endPoint;
        }

        private SolidBrush GetRandomColor()
        {
            var brush = _brushes["random"];

            brush.Color = new Color(_random.Next(0, 256), _random.Next(0, 256), _random.Next(0, 256));

            return brush;
        }

        public void Run()
        {
            _window.Create();
            _window.Show();
            // _window.Join();
        }

        ~GameOverlayManager()
        {
            Dispose(false);
        }

        #region IDisposable Support

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                _window?.Dispose();

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region deprecated
        /// <summary>
        /// deprecated
        /// </summary>
        [Obsolete("deprecated")]
        public void StartLoop()
        {
            _isRunning = true;
            _bgw = new BackgroundWorker();
            _bgw.DoWork += (sender, args) =>
            {
                while (_isRunning)
                {
                    // 1. find process   
                    Thread.Sleep(5000);
                    var processes = System.Diagnostics.Process.GetProcessesByName(GAME_PROCESS);

                    if (processes.Length > 0 && _window == null)
                    {
                        // dr2 has 2 windows during launching...shit
                        processes = System.Diagnostics.Process.GetProcessesByName(GAME_PROCESS);
                        var process = processes.First();
                        try {
                            if (process.MainWindowTitle.StartsWith(GAME_WIN_TITLE, StringComparison.OrdinalIgnoreCase))
                            // only when the game window available.
                                this.InitializeOverlay(process.MainWindowHandle);
                        } catch (Exception ex) {
                            _logger.Trace("Waiting for game window: {0}", GAME_WIN_TITLE);
                        }
                    }

                    if (processes.Length == 0 && _window != null)
                    {
                        // destroy the window
                        _window.Dispose();
                        _window = null;
                    }

                    //if (processes.Length > 0 && _window != null)
                    //{
                    //    // running
                    //    // check full screen ?
                    //    if (WindowHelper.GetForegroundWindow() != _window.Handle)
                    //    {
                    //        try
                    //        {
                    //            Thread.Sleep(3000);
                    //            processes = System.Diagnostics.Process.GetProcessesByName(GAME_PROCESS);
                    //            var process = processes.First();
                    //            WindowHelper.EnableBlurBehind(process.MainWindowHandle);
                    //            _window.IsTopmost = true;
                    //            _window.Recreate();
                    //        }
                    //        catch (Exception e)
                    //        {
                    //            MessageBox.Show("BOOOOOOM");
                    //        }
                    //    }
                    //}
                }
            };
            _bgw.RunWorkerAsync();
        }

        /// <summary>
        /// deprecated
        /// </summary>
        [Obsolete("deprecated")]
        public void StopLoop()
        {
            _bgw?.Dispose();
            _bgw = null;
            _window?.Dispose();
            _window = null;
            _isRunning = false;
        }

        #endregion

    }


    public class GameOverlayDrawingHelper {

        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        public Point getPoint(float x, float y)
        {
            return new Point(x, y);
        }

        public Rectangle getRectangle(float x, float y, float right, float bottom)
        {
            return new Rectangle(x, y, right, bottom);
        }

        public Color getColor(byte r, byte g, byte b, byte a)
        {
            return new Color(r, g, b, a);
        }

        public Point drawSector(Graphics gfx, 
            string unit,
            float x, 
            float y, 
            float width, 
            float height, 
            float value, 
            float maxValue, 
            IBrush bgBrush,
            IBrush valueBrush,
            IBrush emptyBrush,
            Font textFont,
            float thicknessRatio=0.2f)
        {
            if (value > maxValue)
            {
                value = maxValue;
            }
            if (value < 0)
            {
                value = 0;
            }
            var centerX = x + 0.5f * width;
            var centerY = y + 0.5f * height;
            var radiusOuter = MathF.Min(width, height) * 0.5f;
            var radiusInner = radiusOuter * (1 - thicknessRatio);
            // draw backgroud geometry
            var geo_bg = gfx.CreateGeometry();
            var pointOuterStart = new Point(
                centerX + radiusOuter * MathF.Cos(MathF.PI * 1.25f), 
                centerY - radiusOuter * MathF.Sin(MathF.PI * 1.25f)
            );
            var pointOuterEnd = new Point(
                centerX + radiusOuter * MathF.Cos(MathF.PI * 1.75f),
                centerY - radiusOuter * MathF.Sin(MathF.PI * 1.75f)
            );
            var pointInnerEnd = new Point(
                centerX + radiusInner * MathF.Cos(MathF.PI * 1.75f),
                centerY - radiusInner * MathF.Sin(MathF.PI * 1.75f)
            );
            var pointInnerStart = new Point(
                centerX + radiusInner * MathF.Cos(MathF.PI * 1.25f),
                centerY - radiusInner * MathF.Sin(MathF.PI * 1.25f)
            );
            _logger.Info("radiusOuter: {0}, radiusInner: {1}", radiusOuter, radiusInner);
            _logger.Info("pointOuterStart: {0}, pointOuterEnd: {1}, pointInnerEnd: {2}, pointInnerStart: {3}", pointOuterStart, pointOuterEnd, pointInnerEnd, pointInnerStart);
            
            geo_bg.BeginFigure(
                pointOuterStart,
                true
                );
            geo_bg.AddCurve(
                pointOuterEnd,
                radiusOuter, ArcSize.Large
            );
            geo_bg.AddPoint(
                pointInnerEnd
            );
            geo_bg.AddCurve(
                pointInnerStart,
                radiusInner, ArcSize.Large, SweepDirection.CounterClockwise
            );
            geo_bg.EndFigure();
            geo_bg.Close();
            gfx.FillGeometry(geo_bg, emptyBrush);
            gfx.DrawGeometry(geo_bg, bgBrush, 1);
            
            // draw value
            var angle = MathF.PI * 1.25f - value / maxValue * MathF.PI * 1.5f;
            var arcSize = value / maxValue * 270f >= 180f ? ArcSize.Large : ArcSize.Small;
            radiusOuter -= 1;
            radiusInner += 1;
            geo_bg = gfx.CreateGeometry();
            geo_bg.BeginFigure(
                new Point(
                    centerX + radiusOuter * MathF.Cos(MathF.PI * 1.25f), 
                    centerY - radiusOuter * MathF.Sin(MathF.PI * 1.25f)
                ),
                true
            );
            geo_bg.AddCurve(
                new Point(
                    centerX + radiusOuter * MathF.Cos(angle),
                    centerY - radiusOuter * MathF.Sin(angle)
                ),
                radiusOuter, arcSize
            );
            geo_bg.AddPoint(
                new Point(
                    centerX + radiusInner * MathF.Cos(angle),
                    centerY - radiusInner * MathF.Sin(angle)
                )
            );
            var endPoint = new Point(
                centerX + radiusInner * MathF.Cos(MathF.PI * 1.25f),
                centerY - radiusInner * MathF.Sin(MathF.PI * 1.25f)
            );
            geo_bg.AddCurve(
                endPoint,
                radiusInner, arcSize, SweepDirection.CounterClockwise
            );
            geo_bg.EndFigure();
            geo_bg.Close();
            gfx.FillGeometry(geo_bg, valueBrush);

            gfx.drawTextWithBackgroundCentered(textFont,
                0.2f * radiusOuter,
                valueBrush,
                bgBrush,
                centerX,
                centerY + radiusInner,
                unit);
            gfx.drawTextWithBackgroundCentered(textFont,
                0.45f * radiusOuter,
                valueBrush,
                bgBrush,
                centerX,
                centerY,
                Convert.ToInt32(value).ToString());

            return endPoint;
        }
    }
}


