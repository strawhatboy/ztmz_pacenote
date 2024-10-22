local resources = {};

-- This function is called when the script is loaded
-- args: dotnet object
--     args.Graphics: GameOverlay.Drawing.Graphics
--     args.Config: ZTMZ.PacenoteTool.Base.Config
--     args.I18NLoader: ZTMZ.PacenoteTool.Base.I18NLoader
--     args.GameData: ZTMZ.PacenoteTool.Base.Game.GameData
--     args.GameContext: 
--         args.GameContext.TrackName: string
--         args.GameContext.AudioPackage: string
--         args.GameContext.ScriptAuthor: string
--         args.GameContext.PacenoteType: string
function onInit(args)
    local gfx = args.Graphics;
    local conf = args.Config;
    local _brushes = {};
    if (conf.HudChromaKeyMode) then
        _brushes["green"] = gfx.CreateSolidBrush(0, 0, 255);
    else
        _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
    end

    _brushes["background"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 100);
    _brushes["splitBar"] = gfx.CreateSolidBrush(255, 255, 255);
    _brushes["inprogress"] = gfx.CreateSolidBrush(152, 204, 245);
    _brushes["darkgrey"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F);
    _brushes["darkred"] = gfx.CreateSolidBrush(128, 18, 18);
    _brushes["black"] = gfx.CreateSolidBrush(0, 0, 0);
        
    resources["brushes"] = _brushes;
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
    -- size is width here. the bar's length
    local size = gfx.Height * self.GetConfigByKey("dashboards.settings.size");
    local positionH = self.GetConfigByKey("dashboards.settings.positionH");
    local positionV = self.GetConfigByKey("dashboards.settings.positionV");
    local marginH = self.GetConfigByKey("dashboards.settings.marginH") * gfx.Width;
    local marginV = self.GetConfigByKey("dashboards.settings.marginV") * gfx.Height;
    local whRatio = self.GetConfigByKey("dashboards.settings.whRatio");
    local isVertical = self.GetConfigByKey("dashboards.settings.verticalOrHorizontal");

    -- print("calulating the margin, padding, pos of each element")
    
    -- calculate the margin, padding, pos of each element
    ---- print("calculating the margin, padding, pos of each element"); 
    local centerX = 0;
    local height = size / whRatio;
    if (positionH == -1) then
        -- -1 means left
        centerX = 0 + marginH;
    else
        if (positionH == 1) then
            -- 1 means right
            centerX = gfx.Width - marginH;
        else
            -- 0 means center
            centerX = gfx.Width / 2;
        end
    end
    
    local centerY = 0;
    if (positionV == -1) then
        -- -1 means top
        centerY = 0 + marginV;
    else
        if (positionV == 1) then
            -- 1 means bottom
            centerY = gfx.Height - marginV;
        else
            -- 0 means center
            centerY = gfx.Height / 2;
        end
    end
    local telemetryStartX = centerX - size / 2;
    local telemetryStartY = centerY - height / 2;

    local blocksCount = 2 ^ (math.floor(math.log(data.TrackLength / 1000) / math.log(2)));
    local telemetryEndX = telemetryStartX + size;
    local telemetryEndY = telemetryStartY + height;

    local splitBarWidth = 2;
    local cursorSize = 8;
    local splitBarHeight = height
    -- blocksCount - 1 splitBars
    local splitBarStartPositions = {};
    local splitBarEndPositions = {};
    local barStartPositions = {};
    local barEndPositions = {};
    local finishLineStartX = telemetryStartX + size - splitBarWidth / 2;
    local finishLineStartY = telemetryStartY;
    local finishLineEndX = telemetryStartX + size + splitBarWidth / 2;
    local finishLineEndY = telemetryEndY + height * 2;  -- the finish line is a little bit longer than the bar
    local finishFlagStartX = finishLineEndX;
    local finishFlagStartY = telemetryStartY;
    local finishFlagEndX = finishFlagStartX + height;
    local finishFlagEndY = finishLineEndY;
    -- finish flag is 2x4 rectangles, with 1x1 chequered with black and white
    local finishFlagsStartPositions = {}
    local finishFlagsEndPositions = {}
    for i = 1, 18 do
        finishFlagsStartPositions[i] = { finishFlagStartX + height / 2 * ((i - 1) % 3), finishFlagStartY + height / 2 * math.floor((i - 1) / 3) };
        finishFlagsEndPositions[i] = { finishFlagStartX + height / 2 * ((i - 1) % 3 + 1), finishFlagStartY + height / 2 * (math.floor((i - 1) / 3) + 1) };
    end

    for i = 1, blocksCount - 1 do
        splitBarStartPositions[i] = { telemetryStartX + size / blocksCount * i - splitBarWidth / 2, telemetryStartY };
        splitBarEndPositions[i] = { telemetryStartX + size / blocksCount * i + splitBarWidth / 2, telemetryEndY };
    end
    for i = 1, blocksCount do
        barStartPositions[i] = { telemetryStartX + size / blocksCount * (i - 1) + splitBarWidth / 2, telemetryStartY };
        barEndPositions[i] = { telemetryStartX + size / blocksCount * i - splitBarWidth / 2, telemetryEndY };
    end
    local progressEndX = telemetryStartX + size * data.CompletionRate;
    local progressEndY = telemetryEndY;

    local cursorPoints = {}; -- 5 points
    cursorPoints[1] = { progressEndX, progressEndY + height / 2 + cursorSize / 2 };
    cursorPoints[2] = { progressEndX + cursorSize / 2, progressEndY + height / 2 + cursorSize };
    cursorPoints[3] = { progressEndX + cursorSize / 2, progressEndY + height / 2 + 2 * cursorSize };
    cursorPoints[4] = { progressEndX - cursorSize / 2, progressEndY + height / 2 + 2 * cursorSize };
    cursorPoints[5] = { progressEndX - cursorSize / 2, progressEndY + height / 2 + cursorSize };

    if (isVertical) then
        -- rotate all the positions according to the center
        telemetryStart = getRotatePoint(gfx, helper, centerX, centerY, telemetryStartX, telemetryStartY, -math.pi / 2);
        telemetryStartX = telemetryStart.X;
        telemetryStartY = telemetryStart.Y;
        telemetryEnd = getRotatePoint(gfx, helper, centerX, centerY, telemetryEndX, telemetryEndY, -math.pi / 2);
        telemetryEndX = telemetryEnd.X;
        telemetryEndY = telemetryEnd.Y;
        progressEnd = getRotatePoint(gfx, helper, centerX, centerY, progressEndX, progressEndY, -math.pi / 2);
        progressEndX = progressEnd.X;
        progressEndY = progressEnd.Y;
        for i = 1, blocksCount do
            barStart = getRotatePoint(gfx, helper, centerX, centerY, barStartPositions[i][1], barStartPositions[i][2], -math.pi / 2);
            barStartPositions[i][1] = barStart.X;
            barStartPositions[i][2] = barStart.Y;
            barEnd = getRotatePoint(gfx, helper, centerX, centerY, barEndPositions[i][1], barEndPositions[i][2], -math.pi / 2);
            barEndPositions[i][1] = barEnd.X;
            barEndPositions[i][2] = barEnd.Y;
        end
        for i = 1, blocksCount - 1 do
            splitBarStart = getRotatePoint(gfx, helper, centerX, centerY, splitBarStartPositions[i][1], splitBarStartPositions[i][2], -math.pi / 2);
            splitBarStartPositions[i][1] = splitBarStart.X;
            splitBarStartPositions[i][2] = splitBarStart.Y;
            splitBarEnd = getRotatePoint(gfx, helper, centerX, centerY, splitBarEndPositions[i][1], splitBarEndPositions[i][2], -math.pi / 2);
            splitBarEndPositions[i][1] = splitBarEnd.X;
            splitBarEndPositions[i][2] = splitBarEnd.Y;
        end
        for i = 1, 5 do
            cursor = getRotatePoint(gfx, helper, centerX, centerY, cursorPoints[i][1], cursorPoints[i][2], -math.pi / 2);
            cursorPoints[i][1] = cursor.X;
            cursorPoints[i][2] = cursor.Y;
        end
        for i = 1, 18 do
            finishFlagStart = getRotatePoint(gfx, helper, centerX, centerY, finishFlagsStartPositions[i][1], finishFlagsStartPositions[i][2], -math.pi / 2);
            finishFlagsStartPositions[i][1] = finishFlagStart.X;
            finishFlagsStartPositions[i][2] = finishFlagStart.Y;
            finishFlagEnd = getRotatePoint(gfx, helper, centerX, centerY, finishFlagsEndPositions[i][1], finishFlagsEndPositions[i][2], -math.pi / 2);
            finishFlagsEndPositions[i][1] = finishFlagEnd.X;
            finishFlagsEndPositions[i][2] = finishFlagEnd.Y;
        end
        finishLineStart = getRotatePoint(gfx, helper, centerX, centerY, finishLineStartX, finishLineStartY, -math.pi / 2);
        finishLineStartX = finishLineStart.X;
        finishLineStartY = finishLineStart.Y;
        finishLineEnd = getRotatePoint(gfx, helper, centerX, centerY, finishLineEndX, finishLineEndY, -math.pi / 2);
        finishLineEndX = finishLineEnd.X;
        finishLineEndY = finishLineEnd.Y;
    end

    -- print("drawing the background")
    gfx.FillRectangle(_brushes["background"], telemetryStartX, telemetryStartY, telemetryEndX, telemetryEndY);

    -- print("drawing current progress")
    for i = 1, blocksCount do
        if (isVertical) then
            -- if progressEndY is greater than barEndPositions[i][2], then fill the bar with _brushes["inprogress"] till progressEndY, and break the loop;
            -- else fill the bar with _brushes["darkgrey"] till barEndPositions[i][2]
            if (progressEndY > barEndPositions[i][2]) then
                gfx.FillRectangle(_brushes["inprogress"], barStartPositions[i][1], barStartPositions[i][2], barEndPositions[i][1], progressEndY);
                break;
            else
                gfx.FillRectangle(_brushes["darkgrey"], barStartPositions[i][1], barStartPositions[i][2], barEndPositions[i][1], barEndPositions[i][2]);
            end
        else
            -- if progressEndX is greater than barEndPositions[i][1], then fill the bar with _brushes["inprogress"] till progressEndX, and break the loop;
            -- else fill the bar with _brushes["darkgrey"] till barEndPositions[i][1]
            if (progressEndX > barEndPositions[i][1]) then
                gfx.FillRectangle(_brushes["inprogress"], barStartPositions[i][1], barStartPositions[i][2], progressEndX, barEndPositions[i][2]);
                break;
            else
                gfx.FillRectangle(_brushes["darkgrey"], barStartPositions[i][1], barStartPositions[i][2], barEndPositions[i][1], barEndPositions[i][2]);
            end
        end
    end

    -- print("drawing split bars")
    for i = 1, blocksCount - 1 do
        gfx.FillRectangle(_brushes["splitBar"], splitBarStartPositions[i][1], splitBarStartPositions[i][2], splitBarEndPositions[i][1], splitBarEndPositions[i][2]);
    end

    -- print("drawing finish line")
    gfx.FillRectangle(_brushes["darkred"], finishLineStartX, finishLineStartY, finishLineEndX, finishLineEndY);
    -- print("drawing finish flag")
    local isBlack = false;
    for i = 1, 18 do
        if (isBlack) then
            gfx.FillRectangle(_brushes["black"], finishFlagsStartPositions[i][1], finishFlagsStartPositions[i][2], finishFlagsEndPositions[i][1], finishFlagsEndPositions[i][2]);
            isBlack = false;
        else
            gfx.FillRectangle(_brushes["splitBar"], finishFlagsStartPositions[i][1], finishFlagsStartPositions[i][2], finishFlagsEndPositions[i][1], finishFlagsEndPositions[i][2]);
            isBlack = true;
        end
    end

    -- print("drawing the cursor")
    local geo_path = gfx.CreateGeometry();
    geo_path.BeginFigure(helper.getPoint(cursorPoints[1][1], cursorPoints[1][2]), true);
    geo_path.AddPoint(helper.getPoint(cursorPoints[2][1], cursorPoints[2][2]));
    geo_path.AddPoint(helper.getPoint(cursorPoints[3][1], cursorPoints[3][2]));
    geo_path.AddPoint(helper.getPoint(cursorPoints[4][1], cursorPoints[4][2]));
    geo_path.AddPoint(helper.getPoint(cursorPoints[5][1], cursorPoints[5][2]));
    geo_path.EndFigure();
    geo_path.Close();

    gfx.FillGeometry(geo_path, _brushes["darkred"]);
    gfx.DrawGeometry(geo_path, _brushes["splitBar"], 2);

    -- need manually dispose this object!
    geo_path.Dispose();

end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
end
