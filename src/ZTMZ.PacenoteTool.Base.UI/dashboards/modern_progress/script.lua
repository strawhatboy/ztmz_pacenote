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
    _brushes["splitBar"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 200);
    _brushes["white"] = gfx.CreateSolidBrush(255, 255, 255);
        
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
    local telemetryStartX = 0;
    local height = size / whRatio;
    if (positionH == -1) then
        -- -1 means left
        telemetryStartX = 0 + marginH;
    else
        if (positionH == 1) then
            -- 1 means right
            telemetryStartX = gfx.Width - marginH;
        else
            -- 0 means center
            telemetryStartX = gfx.Width / 2 - size / 2;
        end
    end
    
    local telemetryStartY = 0;
    if (positionV == -1) then
        -- -1 means top
        telemetryStartY = 0 + marginV;
    else
        if (positionV == 1) then
            -- 1 means bottom
            telemetryStartY = gfx.Height - marginV;
        else
            -- 0 means center
            telemetryStartY = gfx.Height / 2 - height / 2;
        end
    end
    local centerX = telemetryStartX + size / 2;
    local centerY = telemetryStartY + height / 2;

    local blocksCount = 2 ^ (math.floor(math.log(data.TrackLength / 1000) / math.log(2)));
    local telemetryEndX = telemetryStartX + size;
    local telemetryEndY = telemetryStartY + height;

    local splitBarWidth = 5;
    local splitBarHeight = height
    -- blocksCount - 1 splitBars
    local splitBarStartPositions = {};
    local splitBarEndPositions = {};
    for i = 1, blocksCount - 1 do
        splitBarStartPositions[i] = { telemetryStartX + size / blocksCount * i - splitBarWidth / 2, telemetryStartY };
        splitBarEndPositions[i] = { telemetryStartX + size / blocksCount * i + splitBarWidth / 2, telemetryEndY };
    end
    local progressEndX = telemetryStartX + size * data.CompletionRate;
    local progressEndY = telemetryEndY;

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
        for i = 1, blocksCount - 1 do
            splitBarStart = getRotatePoint(gfx, helper, centerX, centerY, splitBarStartPositions[i][1], splitBarStartPositions[i][2], -math.pi / 2);
            splitBarStartPositions[i][1] = splitBarStart.X;
            splitBarStartPositions[i][2] = splitBarStart.Y;
            splitBarEnd = getRotatePoint(gfx, helper, centerX, centerY, splitBarEndPositions[i][1], splitBarEndPositions[i][2], -math.pi / 2);
            splitBarEndPositions[i][1] = splitBarEnd.X;
            splitBarEndPositions[i][2] = splitBarEnd.Y;
        end
    end

    -- print("drawing the background")
    gfx.FillRectangle(_brushes["background"], telemetryStartX, telemetryStartY, telemetryEndX, telemetryEndY);

    -- print("drawing current progress")
    gfx.FillRectangle(_brushes["white"], telemetryStartX, telemetryStartY, progressEndX, progressEndY);

    -- print("drawing split bars")
    for i = 1, blocksCount - 1 do
        gfx.FillRectangle(_brushes["splitBar"], splitBarStartPositions[i][1], splitBarStartPositions[i][2], splitBarEndPositions[i][1], splitBarEndPositions[i][2]);
    end

end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
end
