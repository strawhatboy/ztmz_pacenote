local resources = {};

function onInit(gfx, conf)
    local _brushes = {};
    _brushes["black"] = gfx.CreateSolidBrush(0, 0, 0);
    _brushes["white"] = gfx.CreateSolidBrush(255, 255, 255);
    _brushes["red"] = gfx.CreateSolidBrush(255, 0, 0);
    _brushes["grey"] = gfx.CreateSolidBrush(64, 64, 64);
    if (conf.HudChromaKeyMode) then
        _brushes["green"] = gfx.CreateSolidBrush(0, 0, 255);
        _brushes["blue"] = gfx.CreateSolidBrush(255, 0, 255);
        _brushes["clear"] = gfx.CreateSolidBrush(0, 255, 0);
    else
        _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
        _brushes["blue"] = gfx.CreateSolidBrush(0, 0, 255);
        _brushes["clear"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
    end
    _brushes["background"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 100);
    _brushes["telemetryBackground"] = gfx.CreateSolidBrush(0x3, 0x6, 0xF, 255);
    
    local _fonts = {};
    _fonts["consolas"] = gfx.CreateFont("Consolas", 14);
    _fonts["telemetryGear"] = gfx.CreateFont("Consolas", 32);
    
    resources["brushes"] = _brushes;
    resources["fonts"] = _fonts;
end

function onUpdate(gfx, conf, data)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    gfx.DrawTextWithBackground(_fonts["consolas"], _brushes["green"], _brushes["background"], 0, 0, data.ToString());
end

function onExit()
    for k,v in resources["brushes"] do
        v.Dispose();
    end
end