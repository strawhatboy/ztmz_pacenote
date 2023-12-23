local resources = {};

function onInit(gfx, conf)
    local _brushes = {};
    if (conf.HudChromaKeyMode) then
        _brushes["green"] = gfx.CreateSolidBrush(0, 0, 255);
    else
        _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
    end
    _brushes["background"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 100);

    local _fonts = {};
    _fonts["consolas"] = gfx.CreateFont("Consolas", 14);
        
    resources["brushes"] = _brushes;
    resources["fonts"] = _fonts;
end

function onUpdate(gfx, conf, data, ctx)
    local padding = 200;
    local _fonts = resources["fonts"];
    local _brushes = resources["brushes"];
    local infoText = "TrackName:\t" .. ctx.TrackName .. "\n" ..
        "AudioPackage:\t" .. ctx.AudioPackage .. "\n" ..
        "ScriptAuthor:\t" .. ctx.ScriptAuthor .. "\n" ..
        "IsDyanmic:\t" .. ctx.PacenoteType;
    local size = gfx.MeasureString(_fonts["consolas"], _fonts["consolas"].FontSize, infoText);
    gfx.DrawTextWithBackground(_fonts["consolas"], _brushes["green"], _brushes["background"], gfx.Width - size.X, 0, infoText);
end

function onExit()
    for k,v in resources["brushes"] do
        v.Dispose();
    end
    for k,v in resources["fonts"] do
        v.Dispose();
    end
end
