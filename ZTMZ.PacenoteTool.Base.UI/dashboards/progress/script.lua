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
        
    resources["brushes"] = _brushes;
end

function onUpdate(args)
    local gfx = args.Graphics;
    local conf = args.Config;
    local data = args.GameData;
    local _brushes = resources["brushes"];
    gfx.FillRectangle(_brushes["green"], 
                0, 
                gfx.Height - 5, 
                data.CompletionRate * gfx.Width,
                gfx.Height);
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
end
