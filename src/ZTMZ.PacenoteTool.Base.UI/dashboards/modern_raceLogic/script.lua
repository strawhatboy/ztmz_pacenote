local resources = {};
local MAX_SPEED = 200;
local MAX_WHEEL_SPEED = 220;
local MAX_WHEEL_TEMP = 800;
local MAX_G_FORCE = 2.2;
local MAX_SUSPENSION_SPD = 1000; -- m/s
local MIN_SUSPENSION_SPD = -1000;
local MAX_SUSPENSION = 200;
local MIN_SUSPENSION = -200;

local SWEEPDIRECTION_COUNTERCLOCKWISE = 0;
local SWEEPDIRECTION_CLOCKWISE = 1;

local ARCSIZE_SMALL = 0;
local ARCSIZE_LARGE = 1;

-- This function is called when the script is loaded
-- args: dotnet object
--     args.Graphics: GameOverlay.Drawing.Graphics
--     args.Config: ZTMZ.PacenoteTool.Base.Config
--     args.I18NLoader: ZTMZ.PacenoteTool.Base.I18NLoader
--     args.GameData: ZTMZ.PacenoteTool.Base.Game.GameData
--     args.GameOverlayDrawingHelper: ZTMZ.PacenoteTool.GameOverlayDrawingHelper
--     args.GameContext: 
--         args.GameContext.TrackName: string
--         args.GameContext.AudioPackage: string
--         args.GameContext.ScriptAuthor: string
--         args.GameContext.PacenoteType: string

function onInit(args)
    local gfx = args.Graphics;
    local conf = args.Config;
    local helper = args.GameOverlayDrawingHelper;
    local _brushes = {};
    _brushes["black"] = gfx.CreateSolidBrush(0, 0, 0);
    _brushes["white"] = gfx.CreateSolidBrush(255, 255, 255);
    _brushes["red"] = gfx.CreateSolidBrush(255, 0, 0);
    _brushes["grey"] = gfx.CreateSolidBrush(64, 64, 64);
    if (conf.HudChromaKeyMode) then
        _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
        _brushes["blue"] = gfx.CreateSolidBrush(255, 0, 255);
        _brushes["clear"] = gfx.CreateSolidBrush(0, 255, 0);
    else
        _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
        _brushes["blue"] = gfx.CreateSolidBrush(0, 0, 255);
        _brushes["clear"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
    end

    _brushes["border"] = gfx.CreateSolidBrush(0x5d, 0x5b, 0x58, 100);
    _brushes["background"] = gfx.CreateSolidBrush(0, 0, 0, 255);
    _brushes["transparent"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
    _brushes["telemetryBackground"] = gfx.CreateSolidBrush(0x2c, 0x33, 0x3c, 255);
    _brushes["theme"] = gfx.CreateSolidBrush(0xfc, 0x4a, 0x01, 255);
    _brushes["themeRG"] = gfx.CreateRadialGradientBrush(
        helper.getColor(0xfc, 0x4a, 0x01, 255),
        helper.getColor(0xfc, 0x4a, 0x01, 200),
        helper.getColor(0xfc, 0x4a, 0x01, 0));
    
    _brushes["greenLED"] = gfx.CreateSolidBrush(0, 255, 0, 200);
    
    local _fonts = {};
    _fonts["consolas"] = gfx.CreateFont("Consolas", 14);
    _fonts["wrc"] = gfx.CreateCustomFont("WRC Clean", 14);
    _fonts["wrcGear"] = gfx.CreateCustomFont("WRC Clean Roman", 32);
    _fonts["telemetryGear"] = gfx.CreateFont("Consolas", 32);
    _fonts["wrcBold"] = gfx.CreateFont("WRC Clean Bold", 14, "bold");
    
    resources["brushes"] = _brushes;
    resources["fonts"] = _fonts;
end

function drawRacelogicTelemetry(gfx, self, data, ctx, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    
    -- Draw background image
    gfx.DrawImage(self.ImageResources["images@background"], x, y, x + width, y + height);
    
    -- Draw current race time
    local raceTime = data.LapTime;
    local raceTimeStrMinute = math.floor(raceTime / 60);
    local raceTimeStrSecond = math.floor(raceTime % 60);
    local raceTimeMilisecond = math.floor((raceTime - math.floor(raceTime)) * 1000);
    if (raceTimeStrMinute < 10) then
        raceTimeStrMinute = "0" .. raceTimeStrMinute;
    end
    if (raceTimeStrSecond < 10) then
        raceTimeStrSecond = "0" .. raceTimeStrSecond;
    end
    if (raceTimeMilisecond < 10) then
        raceTimeMilisecond = "00" .. raceTimeMilisecond;
    elseif (raceTimeMilisecond < 100) then
        raceTimeMilisecond = "0" .. raceTimeMilisecond;
    end
    
    -- Adjust the size multiplier based on the background aspect ratio
    local multiVal = 0.34;
    local charSpacing = -height * 0.025;

    local raceTimeStr = raceTimeStrMinute .. ":" .. raceTimeStrSecond .. "." .. raceTimeMilisecond;
    local size = gfx.MeasureString(_fonts["wrcBold"], height * multiVal, raceTimeStr);
    -- Center the time horizontally and vertically
    local totalWidth = size.X + (charSpacing * (#raceTimeStr - 1));
    local xPos = x + (width - totalWidth) / 2.75;
    local yPos = y + (height - size.Y) / 2.16;
    
    for i = 1, #raceTimeStr do
        local char = raceTimeStr:sub(i, i);
        gfx.DrawText(_fonts["wrc"], height * multiVal, _brushes["white"], xPos, yPos, char);
        local charSize = gfx.MeasureString(_fonts["wrc"], height * multiVal, char);
        xPos = xPos + charSize.X + charSpacing;
    end
end

function onUpdate(args)
    local data = args.GameData;
    local gfx = args.Graphics;
    local conf = args.Config;
    local ctx = args.GameContext;
    local i18n = args.I18NLoader;
    local helper = args.GameOverlayDrawingHelper;
    local self = args.Self

    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    
    -- calculate the margin, padding, pos of each element
    local size = gfx.Height * self.GetConfigByKey("dashboards.settings.size") / 4; -- height is halved
    local positionH = self.GetConfigByKey("dashboards.settings.positionH");
    local positionV = self.GetConfigByKey("dashboards.settings.positionV");
    local marginH = self.GetConfigByKey("dashboards.settings.marginH") * gfx.Width;
    local marginV = self.GetConfigByKey("dashboards.settings.marginV") * gfx.Height;
    local whRatio = 672 / 284; --self.GetConfigByKey("dashboards.settings.whRatio");

    local telemetryStartX = 0;
    if (positionH == -1) then
        telemetryStartX = 0 + marginH;
    elseif (positionH == 1) then
        telemetryStartX = gfx.Width - size * whRatio - marginH;
    else
        telemetryStartX = gfx.Width / 2 - size * whRatio / 2;
    end
    
    local telemetryStartY = 0;
    if (positionV == -1) then
        telemetryStartY = 0 + marginV;
    elseif (positionV == 1) then
        telemetryStartY = gfx.Height - size - marginV;
    else
        telemetryStartY = gfx.Height / 2 - size / 2;
    end
    local width = size * whRatio * 1; -- increase width to fit all elements

    drawRacelogicTelemetry(gfx, self, data, ctx, helper, telemetryStartX, telemetryStartY, width, size);

    -- Check if best lap comparison is enabled and in racing state
    local showBestReplay = self.GetConfigByKey("dashboards.settings.showLocalBest") and ctx.LocalReplayValid;
    local showDeltaTime = self.GetConfigByKey("dashboards.settings.showDeltaTime");
    local deltaTime = 0;
    if showBestReplay and ctx.GameState == GAMESTATE_Racing then
        deltaTime = ctx.GetDeltaByDistanceAndTime:Invoke(data.LapDistance, data.LapTime);
    end

    -- Draw delta time with styled background
    if showBestReplay and ctx.GameState == GAMESTATE_Racing and showDeltaTime then
        -- Format delta time text
        local deltaText = string.format("%s%.2f", deltaTime >= 0 and "+" or "-", math.abs(deltaTime));
        
        -- Create brushes for delta time display
        local deltaBrush = deltaTime <= 0 
            and gfx.CreateSolidBrush(0, 128, 0)    -- Green for ahead
            or gfx.CreateSolidBrush(255, 0, 0);    -- Red for behind
        
        -- Calculate text position and size
        local deltaWeight = size * 0.12;
        local deltaX = telemetryStartX + width * 0.72;
        local deltaY = telemetryStartY + size * 0.38;
        
        -- Calculate text dimensions for background
        local textSize = gfx.MeasureString(_fonts["wrc"], deltaWeight, deltaText);
        local padding = deltaWeight * 0.3;
        local rectX = deltaX - (textSize.X / 2) - padding;
        local rectY = deltaY;
        local rectWidth = textSize.X + (padding * 2);
        local rectHeight = textSize.Y + (padding / 2);
        local cornerRadius = rectHeight / 5.5;
        
        -- Draw rounded rectangle background
        gfx.FillRoundedRectangle(deltaBrush, 
            rectX, 
            rectY, 
            rectX + rectWidth,
            rectY + rectHeight,
            cornerRadius);
        
        -- Draw delta time text
        gfx.DrawText(_fonts["wrc"], 
            deltaWeight, 
            _brushes["white"],
            rectX + padding,
            rectY + (padding / 4),
            deltaText);
            
        -- Dispose of temporary brush
        deltaBrush.Dispose();
    end

    -- LED display logic
    local ledSize = size * 0.043
    local leftLedX = telemetryStartX + width/2 - width*0.11 - ledSize/2
    local rightLedX = telemetryStartX + width/2 + width*0.096 - ledSize/2
    local ledY = telemetryStartY + ledSize * 2.43

    -- Create LED brushes based on delta time
    local ledBrush;
    if deltaTime <= 0 then
        -- Ahead (green)
        ledBrush = gfx.CreateSolidBrush(0, 255, 0, 200);
    else
        -- Behind (red)
        ledBrush = gfx.CreateSolidBrush(255, 0, 0, 200);
    end

    -- Draw LEDs based on delta time
    if math.abs(deltaTime) > 0 then
        -- Draw left LED if delta is between 0 and 1 second
        gfx.FillEllipse(ledBrush, 
            leftLedX - (ledSize - ledSize)/2,
            ledY - (ledSize - ledSize)/2,
            ledSize,
            ledSize
        )

        -- Draw right LED if delta is greater than 1 second
        if math.abs(deltaTime) >= 1.0 then
            gfx.FillEllipse(ledBrush, 
                rightLedX - (ledSize - ledSize)/2,
                ledY - (ledSize - ledSize)/2,
                ledSize,
                ledSize
            )
        end
    end
    
    -- Dispose of the temporary brush
    ledBrush.Dispose();
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
    for k,v in ipairs(resources["fonts"]) do
        v.Dispose();
    end
end