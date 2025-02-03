local resources = {};
local splitDeltas = {};  -- 新增：用于存储每个split点的时间差


    -- 提取厂商名称并返回对应的图片名称
local function extractManufacturer(carName)
    if not carName then return nil end
    
    -- 定义厂商名称与图片文件名的映射
    local manufacturers = {
        ["Toyota"] = "toyota",
        ["Hyundai"] = "Hyundai",
        ["Ford"] = "ford",
        ["Skoda"] = "skoda",
        ["Škoda"] = "skoda",
        ["Volkswagen"] = "vw",
        ["Citroen"] = "citron",
        ["Citroën"] = "citron",
        ["Peugeot"] = "peugeot",
        ["Subaru"] = "subaru",
        ["Mitsubishi"] = "Mithubishi",
        ["Alpine"] = "alpine",
        ["Audi"] = "adui",
        ["BMW"] = "bmw",
        ["Porsche"] = "porsche",
        ["Lancia"] = "lancia",
        ["Mini"] = "Mini",
        ["Abarth"] = "Abarth",
        ["Fiat"] = "fiat",
        ["Honda"] = "honda",
        ["Lada"] = "lada",
        ["Opel"] = "opel",
        ["Renault"] = "renault",
        ["Seat"] = "seat",
        ["Talbot"] = "talbot",
        ["Vauxhall"] = "vauxhall",
        ["MG"] = "MG",
        ["Hillman"] = "hillman",
        ["Lotus"] = "lotus",
        ["Mazda"] = "mazda", 
        ["Aston Martin"] = "astonMartin",
        ["Trabant"] = "trabant",
        ["VW"] = "vw",
        ["Volvo"] = "volvo",
        ["Wartburg"] = "wartburg",
    }
    
    -- 遍历所有厂商名称，检查是否在车名中出现
    for searchName, imageName in pairs(manufacturers) do
        if carName:lower():match(searchName:lower()) then
            return imageName
        end
    end
    
    return nil
end

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
    _brushes["white_transparent"] = gfx.CreateSolidBrush(255, 255, 255, 255*0.4);
    _brushes["red"] = gfx.CreateSolidBrush(255, 0, 0, 200);
    _brushes["grey"] = gfx.CreateSolidBrush(64, 64, 64);
    _brushes["rpm_low"] = gfx.CreateSolidBrush(0x60, 0x9d, 0x51);
    _brushes["rpm_medium"] = gfx.CreateSolidBrush(0xd3, 0xa9, 0x5b);
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
    
    -- 添加新的画笔
    _brushes["theme"] = gfx.CreateSolidBrush(252, 74, 1, 255);
    _brushes["themeRG"] = gfx.CreateRadialGradientBrush(
        helper.getColor(252, 74, 1, 255),
        helper.getColor(252, 74, 1, 200),
        helper.getColor(252, 74, 1, 0));
    
    -- 添加新的时间差背景色画笔
    _brushes["delta_ahead"] = gfx.CreateSolidBrush(0, 128, 0);    -- 绿色
    _brushes["delta_behind"] = gfx.CreateSolidBrush(255, 0, 0);   -- 红色
    
    local _fonts = {};
    _fonts["consolas"] = gfx.CreateFont("Consolas", 14);
    _fonts["wrc"] = gfx.CreateCustomFont("WRC Clean Roman", 14);
    _fonts["wrcBold"] = gfx.CreateCustomFont("WRC Clean", 32);
    _fonts["telemetryGear"] = gfx.CreateFont("Consolas", 32);
    
    resources["brushes"] = _brushes;
    resources["fonts"] = _fonts;
    splitDeltas = {}  -- 重置split点时间差记录
end


-- Helper function to draw background images
local function drawBgImage(gfx, self, imageName, relativeX, relativeY, imgRatio, scale, x, y, width, height)
    local imgHeight = height * scale
    local imgWidth = imgHeight * imgRatio
    local imgX = x + (width * relativeX)
    local imgY = y + (height * relativeY)
    gfx.DrawImage(
        self.ImageResources["images@" .. imageName],
        imgX, 
        imgY, 
        imgX + imgWidth, 
        imgY + imgHeight
    )
end

function drawStaticFrames(gfx, self, data, ctx, helper, x, y, width, height) 
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    

    -- Define hybrid car id
    local hybridCars = {
        "HYBRID", "14", "8", "9", "10", "11", "15", "16", "76", "101", "104"
    }

    -- Check if current car is a hybrid
    local isHybrid = false
    if ctx and ctx.CarName then
        for _, i in ipairs(hybridCars) do
            if i == "HYBRID" then
                -- 对于"HYBRID"，使用完整单词匹配
                if ctx.CarName:upper():match("HYBRID") then
                    isHybrid = true
                    break
                end
            else
                -- 对于数字，只匹配精确的[数字]格式
                if ctx.CarName:match("%[" .. i .. "%]") then
                    isHybrid = true
                    break
                end
            end
        end
    end

    -- Draw background based on car type
    local backgroundImage = isHybrid and "background" or "background2"
    drawBgImage(gfx, self, backgroundImage, 0, 0, 941/100, 0.4, x, y, width, height)
end

-- 分割并绘制驾驶员名字
local function splitDriverName(fullName)
    -- 查找最后一个空格的位置
    local lastSpace = fullName:find("%s[^%s]*$")
    if lastSpace then
        -- 分割名和姓
        local firstName = fullName:sub(1, lastSpace-1)
        local lastName = fullName:sub(lastSpace+1)
        return firstName, lastName
    else
        -- 如果没有空格，返回完整名字作为姓
        return "", fullName
    end
end

-- 清理赛段名称，移除中括号及其内容
local function cleanStageName(name)
    if not isStringEmpty(name) then
        -- 移除中括号及其内容，保留其他文本
        local cleaned = name:gsub("%[.-%]", "")
        -- 移除多余空格并修剪
        cleaned = cleaned:gsub("%s+", " "):match("^%s*(.-)%s*$")
        return cleaned
    end
    return "Unknown Stage"
end

function drawDriverNameAndRegion(gfx, self, data, ctx, helper, x, y, width, height)
    local _fonts = resources["fonts"];
    local _brushes = resources["brushes"];
    
    -- 调整元素大小的比例因子
    local scale = {
        driverName = 0.14,     -- 驾驶员名字大小
        flag = 0.16,          -- 国旗大小
        stageTime = 0.16,     -- 时间显示大小
        progressBar = 0.01,   -- 进度条高度
        stageName = 0.1,      -- 赛段名称大小
        carName = 0.08,       -- 车名大小
        manufacturer = 0.1,   -- 厂商logo高度（增大这个值会让logo变大）
        progressBarWidth = 0.28 -- 进度条宽度（相对于面板宽度的比例）
    }
    
    -- 调整元素位置的比例因子
    local position = {
        -- X轴位置
        driverNameX = 0.03,    -- 驾驶员名字X位置
        flagX = 0.194,         -- 国旗X位置
        timeX = 0.3,          -- 时间显示X位置
        stageNameX = 0.292,    -- 赛段名称X位置
        carNameX = 0.22,      -- 车名X位置
        manufacturerX = 0.242,  -- 厂商logo X位置（增大这个值会向右移动）
        progressBarX = 0.002,  -- 进度条起始位置（从左边算起的比例）
        
        -- Y轴位置
        mainContentY = 0.28,   -- 主要内容的垂直中心线
        progressBarY = 0.02,   -- 进度条位置
        stageNameY = 0.03,    -- 赛段名称位置
        carNameY = 0.4,       -- 车名位置
        manufacturerY = 0.18   -- 厂商logo Y位置（增大这个值会向下移动）
    }

    -- 绘制驾驶员名字
    local driverWeight = scale.driverName * height;
    local driverName = self.GetConfigByKey("dashboards.settings.drivername");
    local firstName, lastName = splitDriverName(driverName)
    
    -- 计算名字和姓氏的宽度
    local firstNameSize = gfx.MeasureString(_fonts["wrc"], driverWeight, firstName);
    local lastNameSize = gfx.MeasureString(_fonts["wrcBold"], driverWeight, lastName);
    
    -- 绘制名字（普通字体）
    local startX = x + width * position.driverNameX;
    if firstName ~= "" then
        gfx.DrawText(_fonts["wrc"], driverWeight, _brushes["white"], 
            startX, 
            y + height * position.mainContentY - firstNameSize.Y / 2, 
            firstName);
        startX = startX + firstNameSize.X + (driverWeight * 0.3); -- 间距为字体大小x
    end
    
    -- 绘制姓氏（粗体）
    gfx.DrawText(_fonts["wrcBold"], driverWeight, _brushes["white"], 
        startX, 
        y + height * position.mainContentY - lastNameSize.Y / 2, 
        lastName);

    -- 绘制国旗
    local driverRegion = "flags@" .. self.GetConfigByKey("dashboards.settings.driverregion");
    if (self.ImageResources.ContainsKey(driverRegion)) then
        local regionImage = self.ImageResources[driverRegion];
        local imageWHRatio = regionImage.Width / regionImage.Height;
        local ImageHeight = scale.flag * height;
        local ImageWidth = ImageHeight * imageWHRatio *1.2;
        local startX = x + width * position.flagX;
        local startY = y + height * position.mainContentY - ImageHeight / 2;
        gfx.DrawImage(regionImage, startX, startY, startX + ImageWidth, startY + ImageHeight);
    end

    -- 绘制赛段时间
    local stageTime = data.LapTime;
    local stageTimeStrMinute = math.floor(stageTime / 60);
    local stageTimeStrSecond = math.floor(stageTime % 60);
    local stageTimeMilisecond = math.floor((stageTime - math.floor(stageTime)) * 1000);
    
    if (stageTimeStrMinute < 10) then stageTimeStrMinute = "0" .. stageTimeStrMinute; end
    if (stageTimeStrSecond < 10) then stageTimeStrSecond = "0" .. stageTimeStrSecond; end
    if (stageTimeMilisecond < 10) then
        stageTimeMilisecond = "00" .. stageTimeMilisecond;
    elseif (stageTimeMilisecond < 100) then
        stageTimeMilisecond = "0" .. stageTimeMilisecond;
    end
    
    local stageTimeStr = stageTimeStrMinute .. ":" .. stageTimeStrSecond .. "." .. stageTimeMilisecond;
    local timeWeight = scale.stageTime * height;
    size = gfx.MeasureString(_fonts["wrcBold"], timeWeight, stageTimeStr);
    gfx.DrawText(_fonts["wrcBold"], timeWeight, _brushes["white"], 
        x + width * position.timeX, 
        y + height * position.mainContentY - size.Y / 2, 
        stageTimeStr);
    
    -- 添加个人最佳成绩差值显示
    local showBestReplay = self.GetConfigByKey("dashboards.settings.showLocalBest") and ctx.LocalReplayValid;
    -- 添加检查：只在比赛进行中显示时间差
    if showBestReplay and ctx.GameState == GAMESTATE_Racing then
        -- 使用 GetDeltaByDistanceAndTime 获取时间差
        local deltaTime = ctx.GetDeltaByDistanceAndTime:Invoke(data.LapDistance, data.LapTime)
        
        -- 格式化显示时间差（秒）
        local deltaText = string.format("%s%.3f s", deltaTime >= 0 and "+" or "-", math.abs(deltaTime))
        
        -- 选择颜色（超前绿色，落后红色）
        local deltaBrush = deltaTime <= 0 and _brushes["delta_ahead"] or _brushes["delta_behind"];
        
        -- 获取主时间显示的尺寸
        local timeWeight = scale.stageTime * height;
        local size = gfx.MeasureString(_fonts["wrcBold"], timeWeight, stageTimeStr);
        
        -- 在主时间下方显示时间差值
        -- gfx.DrawText(_fonts["wrc"], 
        --     timeWeight * 0.8,  -- 稍小的字体大小
        --     deltaBrush,
        --     x + width * position.timeX,
        --     y + height * position.mainContentY + size.Y * 0.7,  -- 在主时间下方
        --     deltaText);
    end
    
    -- 绘制进度条
    local stageProgress = data.CompletionRate;
    if (stageProgress > 1) then stageProgress = 1;
    elseif (stageProgress < 0) then stageProgress = 0; end

    local stageProgressWeight = scale.progressBar * height;
    local stageProgressY = y + height * position.progressBarY;
    local stageProgressX = x + width * position.progressBarX;
    local progressBarWidth = width * scale.progressBarWidth;
    
    -- 绘制进度条背景
    gfx.FillRectangle(_brushes["white_transparent"], 
        stageProgressX, 
        stageProgressY, 
        stageProgressX + progressBarWidth, 
        stageProgressY + stageProgressWeight);
    
    -- 绘制进度条前景
    gfx.FillRectangle(_brushes["theme"], 
        stageProgressX, 
        stageProgressY, 
        stageProgressX + progressBarWidth * stageProgress, 
        stageProgressY + stageProgressWeight);

    -- 计算split点数量和位置
    local trackLength = data.TrackLength / 1000  -- 转换为公里
    local splitCount = math.floor(trackLength / 2.5)  -- 每2.5公里一个split
    
    -- 确保split数量在2到9之间
    splitCount = math.max(2, math.min(9, splitCount))
    
    -- 绘制split点
    for i = 1, splitCount do
        local splitPosition = i / (splitCount + 1)
        local splitX = stageProgressX + progressBarWidth * splitPosition
        local splitY = stageProgressY + stageProgressWeight / 2
        
        -- 根据当前进度决定是否为实心
        local hasPassed = stageProgress >= splitPosition
        if hasPassed then
            gfx.FillCircle(_brushes["white"], splitX, splitY, stageProgressWeight * 1.6)
        else
            gfx.DrawCircle(_brushes["white"], splitX, splitY, stageProgressWeight * 1.4, stageProgressWeight * 0.8)
        end
        
        -- 计算该split点对应的距离
        local splitDistance = data.TrackLength * splitPosition
        
        if showBestReplay then
            -- 如果这个split点刚刚被通过且还没有记录时间差
            if hasPassed and not splitDeltas[i] and stageProgress < (splitPosition + 1/(splitCount + 1)) then
                -- 使用 GetDeltaByDistanceAndTime 获取时间差
                splitDeltas[i] = ctx.GetDeltaByDistanceAndTime:Invoke(splitDistance, data.LapTime)
            end
            
            -- 如果有记录的时间差，显示它
            if splitDeltas[i] then
                local deltaTime = splitDeltas[i]
                local deltaText = string.format("%s%.1f", deltaTime >= 0 and "+" or "-", math.abs(deltaTime))
                local bgBrush = deltaTime <= 0 and _brushes["delta_ahead"] or _brushes["delta_behind"];
                
                -- 计算文本尺寸以确定背景矩形大小
                local textSize = gfx.MeasureString(_fonts["wrc"], stageProgressWeight * 7, deltaText)
                local padding = stageProgressWeight * 2
                local rectX = splitX - (textSize.X / 2) - padding
                local rectY = splitY + stageProgressWeight * 2
                local rectWidth = textSize.X + (padding * 2)
                local rectHeight = textSize.Y + (padding / 2)
                local cornerRadius = rectHeight / 5.5
                
                -- 绘制圆角矩形背景和文本
                gfx.FillRoundedRectangle(bgBrush, 
                    rectX, 
                    rectY, 
                    rectX + rectWidth,
                    rectY + rectHeight,
                    cornerRadius)
                
                gfx.DrawText(_fonts["wrc"], 
                    stageProgressWeight * 7, 
                    _brushes["white"],
                    rectX + padding,
                    rectY + (padding / 4),
                    deltaText)
            elseif not hasPassed then
                -- 如果还未经过该split点，显示公里数
                local kmText = string.format("%.1f", trackLength * splitPosition)
                local textSize = gfx.MeasureString(_fonts["wrc"], stageProgressWeight * 1.2, kmText)
                
                gfx.DrawText(_fonts["wrc"], 
                    stageProgressWeight * 7, 
                    _brushes["white"], 
                    splitX - textSize.X - (stageProgressWeight * 4),
                    splitY + stageProgressWeight * 2,
                    kmText)
            end
        else
            -- 如果未经过或未启用最佳成绩比较，显示公里数
            local kmText = string.format("%.1f", trackLength * splitPosition)
            local textSize = gfx.MeasureString(_fonts["wrc"], stageProgressWeight * 1.2, kmText)
            
            gfx.DrawText(_fonts["wrc"], 
                stageProgressWeight * 7, 
                _brushes["white"], 
                splitX - textSize.X - (stageProgressWeight * 4),
                splitY + stageProgressWeight * 2,
                kmText)
        end
    end
    
    -- 添加终点标记
    local finishX = stageProgressX + progressBarWidth
    local finishY = stageProgressY + stageProgressWeight / 2
    
    -- 终点标记也使用相同的逻辑
    if stageProgress >= 1 then
        -- 已到终点，实心圆点
        gfx.FillCircle(_brushes["white"], finishX, finishY, stageProgressWeight * 1.6)
        
        -- 如果启用了最佳成绩比较，显示最终时间差
        if showBestReplay then
            -- 获取最佳成绩的完成时间
            local keys = ctx.LocalReplayDetailsPerTimesDict.Keys:ToList()
            local bestTime = keys:ElementAt(keys.Count - 1)
            local deltaTime = data.LapTime - bestTime
            
            -- 格式化时间差文本
            local deltaText = string.format("%s%.1f", deltaTime >= 0 and "+" or "-", math.abs(deltaTime))
            local bgBrush = deltaTime <= 0 and _brushes["delta_ahead"] or _brushes["delta_behind"];
            
            -- 计算文本尺寸以确定背景矩形大小
            local textSize = gfx.MeasureString(_fonts["wrcBold"], stageProgressWeight * 7, deltaText)
            local padding = stageProgressWeight * 2  -- 文本周围的内边距
            local rectX = finishX - (textSize.X / 2) - padding
            local rectY = finishY + stageProgressWeight * 2
            local rectWidth = textSize.X + (padding * 2)
            local rectHeight = textSize.Y + (padding / 2)
            local cornerRadius = rectHeight / 5.5  -- 圆角半径
            
            -- 绘制圆角矩形背景
            gfx.FillRoundedRectangle(bgBrush, 
                rectX, 
                rectY, 
                rectX + rectWidth,
                rectY + rectHeight,
                cornerRadius)
            
            -- 绘制白色文本
            gfx.DrawText(_fonts["wrc"], 
                stageProgressWeight * 7, 
                _brushes["white"],
                rectX + padding,
                rectY + (padding / 4),
                deltaText)
        end
    else
        -- 未到终点，空心圆环
        gfx.DrawCircle(_brushes["white"], finishX, finishY, stageProgressWeight * 1.4, stageProgressWeight * 0.8)
    end
    
    -- 显示总里程（分开数字和单位）
    local finishNumber = string.format("%.1f", trackLength)
    local finishUnit = "km"
    local numberSize = stageProgressWeight * 7
    local unitSize = numberSize / 3 * 2
    
    -- 只在未完成时显示总里程
    if stageProgress < 1 then
        -- 计算文字宽度
        local numberTextSize = gfx.MeasureString(_fonts["wrc"], numberSize, finishNumber)
        local unitTextSize = gfx.MeasureString(_fonts["wrc"], unitSize, finishUnit)
        local totalWidth = numberTextSize.X + unitTextSize.X
        
        -- 绘制数字和单位，向左偏移并右对齐
        gfx.DrawText(_fonts["wrc"], 
            numberSize, 
            _brushes["white"], 
            finishX - totalWidth + (stageProgressWeight * 7),
            finishY + stageProgressWeight * 2,
            finishNumber)
            
        gfx.DrawText(_fonts["wrc"], 
            unitSize, 
            _brushes["white"], 
            finishX - unitTextSize.X + (stageProgressWeight * 7),
            finishY + stageProgressWeight * 4.5,
            finishUnit)
    end
    
    -- 进度条末端的圆点（当前位置指示器）
    local dotCenterX = stageProgressX + progressBarWidth * stageProgress;
    local dotCenterY = stageProgressY + stageProgressWeight / 2;
    _brushes["themeRG"].SetCenter(dotCenterX, dotCenterY);
    _brushes["themeRG"].SetRadius(stageProgressWeight * 2.8, stageProgressWeight * 3)
    gfx.FillCircle(_brushes["themeRG"], dotCenterX, dotCenterY, stageProgressWeight * 2.5);

    -- 绘制赛段名称
    local stageName = cleanStageName(ctx.TrackName);
    local stageNameWeight = scale.stageName * height;
    local maxStageNameWidth = width * 0.107; -- 最大宽度
    
    -- 测量文本宽度
    local size = gfx.MeasureString(_fonts["wrc"], stageNameWeight, stageName);
    
    -- 如果文本太长，缩小字体大小
    while size.X > maxStageNameWidth and stageNameWeight > height * 0.04 do
        stageNameWeight = stageNameWeight * 0.95;
        size = gfx.MeasureString(_fonts["wrc"], stageNameWeight, stageName);
    end
    
    -- 定义显示区域
    local stageNameDisplayWidth = width * 0.107; -- 显示区域宽度
    local centerX = x + width * position.stageNameX;
    local textX = centerX + (stageNameDisplayWidth - size.X) / 2;
    
    -- 绘制赛段名称文字（居中对齐）
    gfx.DrawText(_fonts["wrc"], stageNameWeight, _brushes["white"], 
        textX,  -- 使用计算出的居中X坐标
        y + height * position.stageNameY, 
        stageName);
        
    -- 绘制车名和厂商logo
    if not isStringEmpty(ctx.CarName) then
        -- 绘制车名
        local carNameWeight = scale.carName * height;
        local maxCarNameWidth = width * 0.107;
        
        local carNameSize = gfx.MeasureString(_fonts["wrc"], carNameWeight, ctx.CarName);
        
        while carNameSize.X > maxCarNameWidth and carNameWeight > height * 0.04 do
            carNameWeight = carNameWeight * 0.95;
            carNameSize = gfx.MeasureString(_fonts["wrc"], carNameWeight, ctx.CarName);
        end
        
        -- gfx.DrawText(_fonts["wrc"], carNameWeight, _brushes["white"], 
        --     x + width * position.carNameX, 
        --     y + height * position.carNameY, 
        --     ctx.CarName);

        -- 提取并绘制厂商logo
        local manufacturerImage = extractManufacturer(ctx.CarName)
        if manufacturerImage then
            local logoPath = "manufacturers@" .. manufacturerImage
            if (self.ImageResources.ContainsKey(logoPath)) then
                local logo = self.ImageResources[logoPath]
                
                -- 定义显示区域的大小
                local displayAreaWidth = width * 0.042
                local displayAreaHeight = height * 0.2
                
                -- 计算显示区域的位置
                local displayAreaX = x + width * position.manufacturerX
                local displayAreaY = y + height * position.manufacturerY
                
                -- 绘制显示区域的背景（用于调试）
                -- gfx.FillRectangle(_brushes["red"], 
                --     displayAreaX, 
                --     displayAreaY, 
                --     displayAreaX + displayAreaWidth, 
                --     displayAreaY + displayAreaHeight)
                
                -- 计算基于宽度和高度的缩放比例
                local scaleByWidth = displayAreaWidth / logo.Width
                local scaleByHeight = displayAreaHeight / logo.Height
                
                -- 使用较小的缩放比例来确保图片完全适应显示区域
                local finalScale = math.min(scaleByWidth, scaleByHeight)
                
                -- 计算最终的logo尺寸
                local logoWidth = logo.Width * finalScale
                local logoHeight = logo.Height * finalScale
                
                -- 在显示区域内居中
                local logoX = displayAreaX + (displayAreaWidth - logoWidth) / 2
                local logoY = displayAreaY + (displayAreaHeight - logoHeight) / 2
                
                -- 绘制logo
                gfx.DrawImage(logo, 
                    logoX, 
                    logoY, 
                    logoX + logoWidth, 
                    logoY + logoHeight)
            end
        end
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

    -- Reset splitDeltas when race begins
    if ctx.GameState == GAMESTATE_RaceBegin or ctx.GameState == GAMESTATE_AdHocRaceBegin then
        splitDeltas = {}
    end

    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];

    local whRatio = 941.0 / 100.0;
    local switchGearNSpeed = self.GetConfigByKey("dashboards.settings.switchGearNSpeed")
    local telemetryStartX, telemetryStartY, width, height = getDashboardPositionStart(self, gfx, whRatio);

    local useOffset = self.GetConfigByKey("dashboards.settings.useOffset");
    if (useOffset) then
        telemetryStartX = telemetryStartX + 0.6 * width;  -- center the dashboard?
        telemetryStartY = telemetryStartY + 0.0 *height;
    end

    drawStaticFrames(gfx, self, data, ctx, helper, telemetryStartX, telemetryStartY, width, height);
    drawDriverNameAndRegion(gfx, self, data, ctx, helper, telemetryStartX, telemetryStartY, width, height);
    
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
    for k,v in ipairs(resources["fonts"]) do
        v.Dispose();
    end
end
