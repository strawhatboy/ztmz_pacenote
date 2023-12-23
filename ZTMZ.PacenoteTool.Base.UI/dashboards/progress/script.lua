local resources = {};

function onInit(gfx, conf)
    local _brushes = {};
    if (conf.HudChromaKeyMode) then
        _brushes["green"] = gfx.CreateSolidBrush(0, 0, 255);
    else
        _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
    end
        
    resources["brushes"] = _brushes;
end

function onUpdate(gfx, conf, data, ctx)
    local _brushes = resources["brushes"];
    gfx.FillRectangle(_brushes["green"], 
                0, 
                gfx.Height - 5, 
                data.CompletionRate * gfx.Width,
                gfx.Height);
end

function onExit()
    for k,v in resources["brushes"] do
        v.Dispose();
    end
end
