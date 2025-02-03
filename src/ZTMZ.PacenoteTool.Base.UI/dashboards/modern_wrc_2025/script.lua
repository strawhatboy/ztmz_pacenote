local resources = {};

local framesCount = 0;
local bInBlink = true;

-- This function is called when the script is loaded
-- args: dotnet object
--     args.Graphics: GameOverlay.Drawing.Graphics
--     args.Config: ZTMZ.PacenoteTool.Base.Config
--     args.I18NLoader: ZTMZ.PacenoteTool.Base.I18NLoader
--     args.GameData: ZTMZ.PacenoteTool.Base.Game.GameData
--     args.GameOverlayDrawingHelper: ZTMZ.PacenoteTool.GameOverlayDrawingHelper
--     args.Self: ZTMZ.PacenoteTool.Base.UI.Dashboard
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
    _brushes["white_transparent"] = gfx.CreateSolidBrush(255, 255, 255, 255*0.3);
    _brushes["red"] = gfx.CreateSolidBrush(255, 0, 0, 200);
    _brushes["grey"] = gfx.CreateSolidBrush(64, 64, 64);
    _brushes["rpm_low"] = gfx.CreateSolidBrush(0x60, 0x9d, 0x51);
    _brushes["rpm_medium"] = gfx.CreateSolidBrush(0xd3, 0xa9, 0x5b);
    _brushes["delta_ahead"] = gfx.CreateSolidBrush(0, 128, 0);    -- 绿色
    _brushes["delta_behind"] = gfx.CreateSolidBrush(255, 0, 0);   -- 红色
    _brushes["rpm_high"] = gfx.CreateSolidBrush(0xb9, 0x57, 0x4a);
    if (conf.HudChromaKeyMode) then
        _brushes["green"] = gfx.CreateSolidBrush(0, 0, 255);
        _brushes["blue"] = gfx.CreateSolidBrush(255, 0, 255);
        _brushes["clear"] = gfx.CreateSolidBrush(0, 255, 0);
    else
        _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
        _brushes["blue"] = gfx.CreateSolidBrush(0, 0, 255);
        _brushes["clear"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
    end

    _brushes["border"] = gfx.CreateSolidBrush(0x5d, 0x5b, 0x58, 100);
    _brushes["background"] = gfx.CreateSolidBrush(0x00, 0x00, 0x00, 100);
    _brushes["transparent"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
    _brushes["telemetryBackground"] = gfx.CreateRadialGradientBrush(
        helper.getColor(0x2c, 0x33, 0x3c, 150),
        helper.getColor(0x2c, 0x33, 0x3c, 110),
        helper.getColor(0x2c, 0x33, 0x3c, 80),
        helper.getColor(0x2c, 0x33, 0x3c, 0));
    _brushes["rpm"] = gfx.CreateLinearGradientBrush(
        -- green
        helper.getColor(0x31, 0xd2, 0x1b, 255),
        -- yellow
        helper.getColor(0xd3, 0xec, 0x46, 255),
        -- red
        helper.getColor(0xfb, 0x21, 0x0d, 255)
    );
    _brushes["brake"] = gfx.CreateSolidBrush(0xd2, 0x18, 0x1d, 255)
    _brushes["throttle"] = gfx.CreateSolidBrush(0xc3, 0xe1, 0x67, 255)
    -- _brushes["clutch"] = gfx.CreateSolidBrush(0x2a, 0xb4, 0x5d, 255)
    -- _brushes["hybrid"] = gfx.CreateSolidBrush(0x45, 0xf2, 0xee, 255)
    
    local _fonts = {};
    _fonts["consolas"] = gfx.CreateFont("Consolas", 14);
    _fonts["wrc"] = gfx.CreateCustomFont("WRC Clean", 14);
    _fonts["wrcGear"] = gfx.CreateCustomFont("WRC Clean", 32);
    _fonts["telemetryGear"] = gfx.CreateFont("Consolas", 32);
    
    resources["brushes"] = _brushes;
    resources["fonts"] = _fonts;
end

function drawStaticFrames(gfx, self, data, helper, x, y, width, height) 
    -- print("drawing the static frames")
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    -- draw the static frames
    -- 1. background
    -- print("drawing the background")
    
    -- local centerX = x + width * 0.471;
    -- local centerY = y + height * 0.62;
    -- local radius = height * 0.5
    -- _brushes["telemetryBackground"].SetCenter(centerX, centerY);
    -- _brushes["telemetryBackground"].SetRadius(radius, radius);
    -- -- 2. just draw the background (1278x352)
    -- gfx.FillCircle(_brushes["telemetryBackground"], centerX, centerY, radius);
    
    -- Calculate correct dimensions for background image while maintaining aspect ratio
    local imageAspectRatio = 1322 / 768; -- Original image aspect ratio
    local targetAspectRatio = width / height;
    local drawX, drawY, drawWidth, drawHeight = x, y, width, height;
    
    if targetAspectRatio > imageAspectRatio then
        -- Too wide, adjust width
        drawWidth = height * imageAspectRatio;
        drawX = x + (width - drawWidth) / 2; -- Center horizontally
    else
        -- Too tall, adjust height
        drawHeight = width / imageAspectRatio;
        drawY = y + (height - drawHeight) / 2; -- Center vertically
    end
    
    -- Draw the background image with correct proportions
    gfx.DrawImage(self.ImageResources["images@background"], drawX, drawY, drawX + drawWidth, drawY + drawHeight);
end

function drawRPM(gfx, self, data, conf, helper, x, y, width, height)
    local rpm = data.RPM;
    local maxRPM = data.MaxRPM;
    local imageAspectRatio = 768 / 51;
    local targetWidth = width * 0.68; 
    local targetHeight = targetWidth / imageAspectRatio;
    
    -- Calculate position to center the RPM gauge
    local centerX = x + width * 0.5;
    local centerY = y + height * 0.365;
    local rpmStartX = centerX - targetWidth * 0.5;
    local rpmStartY = centerY - targetHeight * 0.5;
    
    -- Calculate clip region based on RPM value
    local rpmPercentage = rpm / maxRPM;
    local rpmClipWidth = targetWidth * rpmPercentage;
    
    -- Draw the RPM gauge
    gfx.ClipRegionStart(rpmStartX, rpmStartY, rpmStartX + rpmClipWidth, rpmStartY + targetHeight);
    gfx.DrawImage(self.ImageResources["images@rpm"], 
        rpmStartX, 
        rpmStartY, 
        rpmStartX + targetWidth, 
        rpmStartY + targetHeight);
    gfx.ClipRegionEnd();
end

function drawThrottle(gfx, self, data, helper, x, y, width, height)
    local throttle = data.Throttle;
    local imageAspectRatio = 768 / 296;
    local targetWidth = width * 0.384;
    local targetHeight = targetWidth / imageAspectRatio;
    
    -- Calculate position
    local widthEnd = x + width * 0.862;
    local heightStart = y + height * 0.843;
    
    -- Calculate clip region based on throttle value
    local throttleClipWidth = targetWidth * (1-throttle);
    gfx.ClipRegionStart(widthEnd - targetWidth, heightStart - targetHeight, widthEnd - throttleClipWidth, heightStart);
    gfx.DrawImage(self.ImageResources["images@throttlebar"], 
        widthEnd - targetWidth, 
        heightStart - targetHeight, 
        widthEnd, 
        heightStart);
    gfx.ClipRegionEnd();
end

function drawBrake(gfx, self, data, helper, x, y, width, height)
    local brake = data.Brake;
    local imageAspectRatio = 768 / 296;
    local targetWidth = width * 0.384;
    local targetHeight = targetWidth / imageAspectRatio;
    
    -- Calculate position
    local widthStart = x + width * 0.141;
    local heightEnd = y + height * 0.843;
    
    -- Calculate clip region based on brake value
    local brakeClipWidth = targetWidth * (1-brake);
    gfx.ClipRegionStart(widthStart + brakeClipWidth, heightEnd - targetHeight, widthStart + targetWidth, heightEnd);
    gfx.DrawImage(self.ImageResources["images@brakebar"], 
        widthStart, 
        heightEnd - targetHeight, 
        widthStart + targetWidth, 
        heightEnd);
    gfx.ClipRegionEnd();
end

function drawHandBrake(gfx, self, data, helper, x, y, width, height)
    local handBrake = data.HandBrake;
    -- Original image size is 40x33
    local imageAspectRatio = 118 / 128;
    local targetWidth = width * 0.128;  -- Adjust size as needed
    local targetHeight = targetWidth / imageAspectRatio;
    
    -- Calculate position
    local iconX = x + width * 0.137;  -- Adjust position as needed
    local iconY = y + height * 0.39;   -- Adjust position as needed
    
    -- Only draw the icon when handbrake is active
    if (handBrake > 0) then
        gfx.DrawImage(self.ImageResources["images@handbrake_active"], 
            iconX, 
            iconY, 
            iconX + targetWidth, 
            iconY + targetHeight);
    end
end

function drawSpeed(gfx, self, data, conf, helper, x, y, width, height, switchGearNSpeed)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];

    local centerX = x + width * 0.5;
    local centerY = y + height * 0.5;

    local speedWeight = height * 0.17;
    local smallSpeedWeight = speedWeight * 0.5;
    
    local speed = math.abs(math.floor(data.Speed));
    local gear = math.floor(data.Gear);
    local maxGear = math.floor(data.MaxGears);
    local speedText = "";
    
    if (switchGearNSpeed) then
        -- 在速度位置显示档位
        speedText = getGear(gear);
        
        -- 计算前后档位的位置
        local prevX = centerX - speedWeight * 0.8;
        local nextX = centerX + speedWeight * 0.8;
        
        if gear == 10 then
            gear = -1;
        end
        -- 显示前一档（修改逻辑）
        if gear == -1 then
            -- 当前是R档, 啥都不显示
        elseif gear == 1 then
            -- 当前是1档，显示N
            gfx.drawTextWithBackgroundCentered(_fonts["wrc"], smallSpeedWeight, _brushes["white_transparent"], _brushes["transparent"], prevX, centerY, "N");
        elseif gear == 0 then
            -- 当前是N档，显示R
            gfx.drawTextWithBackgroundCentered(_fonts["wrc"], smallSpeedWeight, _brushes["white_transparent"], _brushes["transparent"], prevX, centerY, "R");
        elseif gear > 1 then
            -- 其他情况显示数字档位
            local prevGearText = getGear(gear - 1);
            gfx.drawTextWithBackgroundCentered(_fonts["wrc"], smallSpeedWeight, _brushes["white_transparent"], _brushes["transparent"], prevX, centerY, prevGearText);
        end
        
        -- 显示后一档
        if gear < maxGear then
            local nextGearText = getGear(gear + 1);
            gfx.drawTextWithBackgroundCentered(_fonts["wrc"], smallSpeedWeight, _brushes["white_transparent"], _brushes["transparent"], nextX, centerY, nextGearText);
        end
    else
        speedText = tostring(speed);
    end

    local rpmLevel = getRPMLevel(data, conf);

    if (not bInBlink and switchGearNSpeed and gear < maxGear) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["red"], centerX, centerY, speedText);
    else
        gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["transparent"], centerX, centerY, speedText);
    end

    -- RPM level indicators
    -- if (switchGearNSpeed) then
    --     if (rpmLevel == 1) then
    --         gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["rpm_low"], centerX, centerY, speedText);
    --     elseif (rpmLevel == 2) then
    --         gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["rpm_medium"], centerX, centerY, speedText);
    --     elseif (rpmLevel == 3) then
    --         gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["rpm_high"], centerX, centerY, speedText);
    --     end
    -- end
end

function drawGear(gfx, self, data, conf, helper, x, y, width, height, switchGearNSpeed)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];

    local gearWeight = height * 0.13;
    local smallGearWeight = gearWeight * 0.5;
    local gear = math.floor(data.Gear);
    local speed = math.abs(math.floor(data.Speed));
    local gearText = "";
    local maxGear = math.floor(data.MaxGears);
    
    if (switchGearNSpeed) then
        -- 在档位位置显示速度
        gearText = tostring(speed);
    else
        gearText = getGear(gear);
    end

    local gearX = x + width * 0.785;
    local gearY = y + height * 0.51;

    -- 只在显示档位时才显示前后档
    if not switchGearNSpeed then
        -- 前一档位置
        local prevGearX = gearX - gearWeight * 0.65;
        local prevGearY = gearY;
        
        -- 后一档位置
        local nextGearX = gearX + gearWeight * 0.65;
        local nextGearY = gearY;

        if gear == 10 then
            gear = -1;
        end
        -- 显示前一档（修改逻辑）
        if gear == -1 then
            -- 当前是R档, 啥都不显示
            
        elseif gear == 1 then
            -- 当前是1档，显示N
            gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], smallGearWeight, _brushes["white_transparent"], _brushes["transparent"], prevGearX, prevGearY, "N");
        elseif gear == 0 then
            -- 当前是N档，显示R
            gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], smallGearWeight, _brushes["white_transparent"], _brushes["transparent"], prevGearX, prevGearY, "R");
        elseif gear > 1 then
            -- 其他情况显示数字档位
            local prevGearText = getGear(gear - 1);
            gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], smallGearWeight, _brushes["white_transparent"], _brushes["transparent"], prevGearX, prevGearY, prevGearText);
        end

        -- 显示后一档
        if gear < maxGear then
            local nextGearText = getGear(gear + 1);
            gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], smallGearWeight, _brushes["white_transparent"], _brushes["transparent"], nextGearX, nextGearY, nextGearText);
        end
    end

    -- 显示主要数值
    if (not bInBlink and not switchGearNSpeed and gear < maxGear) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], gearWeight, _brushes["white"], _brushes["red"], gearX, gearY, gearText);
    else
        gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], gearWeight, _brushes["white"], _brushes["transparent"], gearX, gearY, gearText);
    end
end

function drawSteeringIndicator(gfx, self, data, helper, x, y, width, height)
    local steeringInput = data.Steering;
    local indicatorRadius = height * 0.01; -- Adjust the size of the red dot
    local initY = y + height * 0.3;
    local centerX = x + width * 0.5;
    local arcWidth = width * 0.33;
    local arcHeight = height * 0.03; -- Adjust the height of the arc
    local centerY = initY - (arcHeight * (1 - (0 * 0))); -- Center position for the arc

    -- Draw the gray dot at the center
    gfx.FillCircle(resources["brushes"]["grey"], centerX, centerY, indicatorRadius);

    -- Calculate the position of the red dot based on steering input
    local indicatorX = centerX + (arcWidth * steeringInput);
    local indicatorY = initY - (arcHeight * (1 - (steeringInput * steeringInput))); -- Quadratic function for arc

    -- Calculate trail parameters based on distance from center
    local distanceFromCenter = math.abs(steeringInput) -- 0 to 1
    local baseTrailCount = 24
    local trailCount = math.floor(baseTrailCount * distanceFromCenter) + 4 -- Minimum 4 trail dots
    local maxTrailLength = width * 0.15; -- Base maximum trail length
    local actualTrailLength = maxTrailLength * distanceFromCenter -- Dynamic trail length
    local baseAlpha = 90;
    local alphaStep = baseAlpha / trailCount;
    
    -- Calculate trail parameters
    local dirX = indicatorX - centerX;
    local dirLength = math.abs(dirX);
    local trailLength = math.min(dirLength, actualTrailLength);
    local stepRatio = trailLength / (arcWidth * math.abs(steeringInput));
    
    -- Draw trails from least opaque to most opaque
    for i = 1, trailCount do
        local t = i / trailCount;
        -- Calculate trail steering input by interpolating back from current position
        local trailSteeringInput = steeringInput * (1 - t * stepRatio);
        -- Calculate trail position using the same arc formula
        local trailX = centerX + (arcWidth * trailSteeringInput);
        local trailY = initY - (arcHeight * (1 - (trailSteeringInput * trailSteeringInput)));
        
        -- Create a temporary brush with calculated alpha
        local alpha = math.floor(baseAlpha - (alphaStep * i));
        local tempBrush = gfx.CreateSolidBrush(255, 0, 0, alpha);
        gfx.FillCircle(tempBrush, trailX, trailY, indicatorRadius * 0.8);
        tempBrush.Dispose();
    end

    -- Draw the main red dot
    gfx.FillCircle(resources["brushes"]["red"], indicatorX, indicatorY, indicatorRadius);
end

function drawDeltaTime(gfx, self, data, ctx, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    
    -- 检查是否启用最佳成绩比较且在比赛状态
    local showBestReplay = self.GetConfigByKey("dashboards.settings.showLocalBest") and ctx.LocalReplayValid;
    if not showBestReplay or ctx.GameState ~= GAMESTATE_Racing then
        return
    end
    
    -- 获取时间差
    local deltaTime = ctx.GetDeltaByDistanceAndTime:Invoke(data.LapDistance, data.LapTime)
    
    -- 格式化显示时间差（秒）
    local deltaText = string.format("%s%.2f", deltaTime >= 0 and "+" or "-", math.abs(deltaTime))
    
    -- 选择颜色（超前绿色，落后红色）
    local deltaBrush = deltaTime <= 0 and _brushes["delta_ahead"] or _brushes["delta_behind"];
    
    -- 设置显示位置（在转速表上方）
    local deltaWeight = height * 0.04;  -- 字体大小
    local deltaX = x + width * 0.295;     -- 水平居中
    local deltaY = y + height * 0.475;   -- 在转速表上方
    
    -- 计算文本尺寸以确定背景矩形大小
    local textSize = gfx.MeasureString(_fonts["wrc"], deltaWeight, deltaText)
    local padding = deltaWeight * 0.3
    local rectX = deltaX - (textSize.X / 2) - padding
    local rectY = deltaY
    local rectWidth = textSize.X + (padding * 2)
    local rectHeight = textSize.Y + (padding / 2)
    local cornerRadius = rectHeight / 5.5
    
    -- 绘制圆角矩形背景
    gfx.FillRoundedRectangle(deltaBrush, 
        rectX, 
        rectY, 
        rectX + rectWidth,
        rectY + rectHeight,
        cornerRadius)
    
    -- 绘制文本
    gfx.DrawText(_fonts["wrc"], 
        deltaWeight, 
        _brushes["white"],
        rectX + padding,
        rectY + (padding / 4),
        deltaText)
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
    
    -- print("calulating the margin, padding, pos of each element")
    
    -- calculate the margin, padding, pos of each element
    ---- print("calculating the margin, padding, pos of each element"); 

    local whRatio = 1322.0 / 768.0;
    local switchGearNSpeed = self.GetConfigByKey("dashboards.settings.switchGearNSpeed")
    local telemetryStartX, telemetryStartY, width, height = getDashboardPositionStart(self, gfx, whRatio);

    local useOffset = self.GetConfigByKey("dashboards.settings.useOffset");
    if (useOffset) then
        telemetryStartX = telemetryStartX + 0.029 * width;  -- center the dashboard?
    end

    drawStaticFrames(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, height);
    drawRPM(gfx, self, data, conf, helper, telemetryStartX, telemetryStartY, width, height);
    drawThrottle(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, height);
    drawBrake(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, height);
    drawHandBrake(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, height);
    drawDeltaTime(gfx, self, data, ctx, helper, telemetryStartX, telemetryStartY, width, height);

    drawSpeed(gfx, self, data, conf, helper, telemetryStartX, telemetryStartY, width, height, switchGearNSpeed);
    drawGear(gfx, self, data, conf, helper, telemetryStartX, telemetryStartY, width, height, switchGearNSpeed);

    local showSteeringIndicator = self.GetConfigByKey("dashboards.settings.showSteeringIndicator")
    if (showSteeringIndicator) then
        drawSteeringIndicator(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, height);
    end
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
    for k,v in ipairs(resources["fonts"]) do
        v.Dispose();
    end
end
