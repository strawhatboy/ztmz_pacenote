local resources = {};

-- This function is called when the script is loaded
-- args: dotnet object
--     args.Graphics: GameOverlay.Drawing.Graphics
--     args.Config: ZTMZ.PacenoteTool.Base.Config
--     args.I18NLoader: ZTMZ.PacenoteTool.Base.I18NLoader
--     args.GameData: ZTMZ.PacenoteTool.Base.Game.GameData
--     args.Self: ZTMZ.PacenoteTool.Base.UI.Dashboard
--     args.GameContext: 
--         args.GameContext.TrackName: string
--         args.GameContext.AudioPackage: string
--         args.GameContext.ScriptAuthor: string
--         args.GameContext.PacenoteType: string
function onInit(args)
    local _brushes = {};
    local gfx = args.Graphics;
    local conf = args.Config;
    if (conf.HudChromaKeyMode) then
        _brushes["green"] = gfx.CreateSolidBrush(0, 0, 255);
    else
        _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
    end
    _brushes["background"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 100);

    local _fonts = {};
    _fonts["consolas"] = gfx.CreateFont("Consolas", 14);
    _fonts["wrc"] = gfx.CreateCustomFont("WRC Clean Roman", 14);
        
    resources["brushes"] = _brushes;
    resources["fonts"] = _fonts;
end

function onUpdate(args)
    local gfx = args.Graphics;
    local conf = args.Config;
    local ctx = args.GameContext;
    local i18n = args.I18NLoader;
    local padding = 200;
    local _fonts = resources["fonts"];
    local _brushes = resources["brushes"];
    local infoText = "FPS:\t" .. gfx.FPS .. "\n" ..
        i18n.ResolveByKey("overlay.track") .. "\t" .. ctx.TrackName .. "\n" ..
        i18n.ResolveByKey("overlay.car") .. "\t" .. ctx.CarName .. "\n" ..
        i18n.ResolveByKey("overlay.audioPackage") .. "\t" .. ctx.AudioPackage .. "\n" ..
        i18n.ResolveByKey("overlay.scriptAuthor") .. "\t" .. ctx.ScriptAuthor;
    local size = gfx.MeasureString(_fonts["wrc"], _fonts["wrc"].FontSize, infoText);
    gfx.DrawTextWithBackground(_fonts["wrc"], _brushes["green"], _brushes["background"], gfx.Width - size.X, 0, infoText);
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
    for k,v in ipairs(resources["fonts"]) do
        v.Dispose();
    end
end
