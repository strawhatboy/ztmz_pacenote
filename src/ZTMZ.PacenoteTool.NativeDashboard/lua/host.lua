-- Native implementation of the existing browser/dashboard host contract.
-- Executed by embedded Lua, once per dashboard. No browser or CLR is involved.
local package, config = ...
local noop = function() end
local static, dynamic, active = {}, {}, nil
local commandCount, dirty = 0, true
local args = {}
local nativeMeasure = __ztmz_measure_text
__ztmz_measure_text = nil
local function emit(command)
    commandCount = commandCount + 1
    assert(commandCount <= 20000, 'dashboard exceeded drawing command limit')
    active[#active + 1] = command
end
local function byte(n) return math.floor(math.max(0, math.min(255, n or 255))) end
local function color(b)
    b = b or {}
    if b.Colors then b = b.Colors[1] or {} end
    local c = b.Color or b
    return (byte(c.A or c.Alpha) << 24) | (byte(c.R or c.Red or 0) << 16)
        | (byte(c.G or c.Green or 0) << 8) | byte(c.B or c.Blue or 0)
end
local function brush(r,g,b,a)
    local values = {R=byte(r), G=byte(g), B=byte(b), A=byte(a)}
    local names = {Red='R', Green='G', Blue='B', Alpha='A'}
    return setmetatable({Dispose=noop}, {
        __index=function(_,key)
            if key=='Color' then return values end
            return values[names[key] or key]
        end,
        __newindex=function(_,key,value)
            if key=='Color' then
                local c=value.Color or value
                values={R=byte(c.R or c.Red),G=byte(c.G or c.Green),B=byte(c.B or c.Blue),A=byte(c.A or c.Alpha)}
            else values[names[key] or key]=value end
        end
    })
end
local function painted(c,b)
    c.color = color(b)
    if b and b.Colors then
        local colors={}
        for i,v in ipairs(b.Colors) do colors[i]=color(v) end
        c.gradient={type=b.Kind, colors=colors, x0=b.StartX, y0=b.StartY,
            x1=b.EndX, y1=b.EndY, centerX=b.CenterX, centerY=b.CenterY,
            radiusX=b.RadiusX, radiusY=b.RadiusY}
    end
    emit(c)
end
local function gradient(kind,...)
    local g={Kind=kind, Colors={...}, StartX=0,StartY=0,EndX=1,EndY=1,
        CenterX=0,CenterY=0,RadiusX=1,RadiusY=1,Dispose=noop}
    g.SetRange=function(x,y,x1,y1) g.StartX=x;g.StartY=y;g.EndX=x1;g.EndY=y1 end
    g.SetCenter=function(x,y) g.CenterX=x;g.CenterY=y end
    g.SetRadius=function(x,y) g.RadiusX=x;g.RadiusY=y end
    return g
end
local function point(x,y) return {X=x,Y=y} end
local function rectangle(l,t,r,b) return {Left=l,Top=t,Right=r,Bottom=b} end
local function rect(l,t,r,b)
    if type(l)=='table' then return l.Left,l.Top,l.Right,l.Bottom end
    return l,t,r,b
end
local gfx={Width=1920,Height=1080,FPS=config.FrameRate or config.frameRate or 60}
args.Graphics=gfx
function gfx.BeginStaticLayer() active=static end
function gfx.EndStaticLayer() active=dynamic end
function gfx.CreateSolidBrush(...) return brush(...) end
function gfx.CreateLinearGradientBrush(...) return gradient('linear',...) end
function gfx.CreateRadialGradientBrush(...) return gradient('radial',...) end
function gfx.CreateFont(name,size,bold,italic)
    return {Name=name,Size=size or 14,FontSize=size or 14,Bold=bold or false,Italic=italic or false,Dispose=noop}
end
gfx.CreateCustomFont=gfx.CreateFont
function gfx.FillRectangle(b,l,t,r,bottom)
    l,t,r,bottom=rect(l,t,r,bottom)
    painted({type='fillRectangle',x=l,y=t,width=r-l,height=bottom-t},b)
end
function gfx.DrawRectangle(b,l,t,r,bottom,thickness)
    if type(l)=='table' then thickness=t end
    l,t,r,bottom=rect(l,t,r,bottom)
    painted({type='drawRectangle',x=l,y=t,width=r-l,height=bottom-t,thickness=thickness or 1},b)
end
function gfx.FillRoundedRectangle(b,l,t,r,bottom,radius)
    painted({type='fillRoundedRectangle',x=l,y=t,width=r-l,height=bottom-t,radius=radius},b)
end
function gfx.DrawRoundedRectangle(b,l,t,r,bottom,radius,thickness)
    painted({type='drawRoundedRectangle',x=l,y=t,width=r-l,height=bottom-t,radius=radius,thickness=thickness or 1},b)
end
function gfx.DrawLine(b,x,y,x1,y1,thickness)
    if type(x)=='table' then thickness=x1;x1=y.X;y1=y.Y;y=x.Y;x=x.X end
    painted({type='drawLine',x1=x,y1=y,x2=x1,y2=y1,thickness=thickness or 1},b)
end
function gfx.FillCircle(b,x,y,r) painted({type='fillCircle',x=x,y=y,radius=r},b) end
function gfx.DrawCircle(b,x,y,r,t) painted({type='drawCircle',x=x,y=y,radius=r,thickness=t or 1},b) end
function gfx.FillEllipse(b,x,y,w,h)
    painted({type='fillEllipse',x=x+w/2,y=y+h/2,radiusX=w/2,radiusY=h/2},b)
end
function gfx.ClipRegionStart(l,t,r,b) emit({type='clipRegionStart',x=l,y=t,width=r-l,height=b-t}) end
function gfx.ClipRegionEnd() emit({type='clipRegionEnd'}) end
function gfx.DrawImage(image,l,t,r,b,opacity)
    if not image then return end
    emit({type='drawImage',imageKey=image.CacheKey,imageUrl=image.Url,x=l,y=t,
        width=r and r-l or image.Width,height=b and b-t or image.Height,opacity=opacity or 1})
end
-- Use the same font files and glyph advances as the native renderer when available.
function gfx.MeasureString(font,size,text)
    if type(size)~='number' then text=size;size=font.Size end
    text=tostring(text or '')
    if nativeMeasure then
        local width,height=nativeMeasure(font.Name,size,text,font.Bold,font.Italic)
        if width then return point(width,height) end
    end
    local width,lines=0,0
    for line in (text..'\n'):gmatch('(.-)\n') do
        local count=utf8.len(line) or #line
        width=math.max(width,count*size*0.6);lines=lines+1
    end
    return point(width,lines*size*1.2)
end
local function textCommand(font,size,b,x,y,text,background)
    local m=gfx.MeasureString(font,size,text)
    painted({type=background and 'drawTextWithBackground' or 'drawText',x=x,y=y,text=tostring(text or ''),
        fontName=font.Name,fontSize=size,fontWeight=font.Bold and 700 or 400,
        fontStyle=font.Italic and 'italic' or 'normal',textWidth=m.X,textHeight=m.Y,
        lineHeight=size*1.2,bgColor=background and color(background) or nil},b)
end
function gfx.DrawText(font,...)
    local a={...}
    if type(a[1])=='number' then textCommand(font,a[1],a[2],a[3],a[4],a[5])
    else textCommand(font,font.Size,a[1],a[2],a[3],a[4]) end
end
function gfx.DrawTextWithBackground(font,b,bg,x,y,text) textCommand(font,font.Size,b,x,y,text,bg) end
function gfx.drawTextWithBackgroundCentered(font,size,b,bg,x,y,text)
    local m=gfx.MeasureString(font,size,text)
    textCommand(font,size,b,x-m.X/2,y-m.Y/2,text,bg)
end
function gfx.CreateGeometry()
    local g={points={},closed=false,Dispose=noop}
    g.BeginFigure=function(p) g.points={{x=p.X,y=p.Y}};g.closed=false end
    g.AddPoint=function(p) g.points[#g.points+1]={x=p.X,y=p.Y} end
    g.AddLine=g.AddPoint
    g.AddCurveWithArcSegmentArgs=function(p,r,large,clockwise)
        local start=g.points[#g.points]
        if not start then g.BeginFigure(p);return end
        local dx,dy=p.X-start.x,p.Y-start.y
        local d=math.sqrt(dx*dx+dy*dy);r=math.abs(r)
        if d<0.01 or r<0.01 or d>2*r then g.AddPoint(p);return end
        local shift=math.sqrt(math.max(0,r*r-d*d/4))
        local sign=((clockwise~=0)==(large~=0)) and 1 or -1
        local cx=(start.x+p.X)/2+sign*shift*dy/d
        local cy=(start.y+p.Y)/2-sign*shift*dx/d
        local first=math.atan(start.y-cy,start.x-cx)
        local sweep=math.atan(p.Y-cy,p.X-cx)-first
        if clockwise~=0 and sweep<0 then sweep=sweep+2*math.pi end
        if clockwise==0 and sweep>0 then sweep=sweep-2*math.pi end
        for i=1,20 do
            local angle=first+sweep*i/20
            g.points[#g.points+1]={x=cx+r*math.cos(angle),y=cy+r*math.sin(angle)}
        end
        g.points[#g.points]={x=p.X,y=p.Y}
    end
    g.AddCurve=g.AddCurveWithArcSegmentArgs
    g.EndFigure=noop
    g.Close=function() g.closed=true end
    return g
end
local function geometryCommand(kind,g,b,thickness)
    local points={}
    for i,v in ipairs(g.points) do points[i]={x=v.x,y=v.y} end
    painted({type=kind,points=points,closed=g.closed,thickness=thickness or 1},b)
end
function gfx.FillGeometry(g,b) geometryCommand('fillGeometry',g,b) end
function gfx.DrawGeometry(g,b,t) geometryCommand('drawGeometry',g,b,t) end
local helper={getColor=brush,getPoint=point,getRectangle=rectangle}
function helper.drawSector(g,unit,x,y,w,h,value,maximum,bg,fill,empty,font,thickness)
    local cx,cy=x+w/2,y+h/2
    local outer=math.max(0,math.min(w,h)/2)
    local inner=outer*(1-math.max(0,math.min(1,thickness or 0.2)))
    local function pos(radius,angle) return point(cx+radius*math.cos(angle),cy-radius*math.sin(angle)) end
    local function sector(ro,ri,finish,large,b)
        local geo=g.CreateGeometry()
        geo.BeginFigure(pos(ro,math.pi*1.25),true)
        geo.AddCurveWithArcSegmentArgs(pos(ro,finish),ro,large,1)
        geo.AddPoint(pos(ri,finish))
        geo.AddCurveWithArcSegmentArgs(pos(ri,math.pi*1.25),ri,large,0)
        geo.Close();g.FillGeometry(geo,b)
        return geo
    end
    local base=sector(outer,inner,math.pi*1.75,1,empty);g.DrawGeometry(base,bg,1)
    value=math.max(0,math.min(value,math.max(maximum,0.0001)))
    local ratio=value/math.max(maximum,0.0001)
    sector(math.max(0,outer-1),inner+1,math.pi*1.25-ratio*math.pi*1.5,ratio>=2/3 and 1 or 0,fill)
    g.drawTextWithBackgroundCentered(font,outer*0.2,fill,bg,cx,cy+inner,unit)
    g.drawTextWithBackgroundCentered(font,outer*0.45,fill,bg,cx,cy,tostring(math.floor(value)))
    return pos(inner+1,math.pi*1.25)
end
args.GameOverlayDrawingHelper=helper
args.Config=config
args.IsWebDashboard=true -- Existing scripts select the portable API with this flag.
args.IsNativeDashboard=true
args.NativeStaticLayerEnabled=false
args.NativeStaticLayerRenderOnly=false
args.WebDashboardStaticLayerDirty=true
local translations={
    ['ui.tb_gamestate']='Game state',['overlay.track']='Track',['overlay.car']='Car',
    ['overlay.carclass']='Class',['overlay.audioPackage']='Audio package',['overlay.scriptAuthor']='Script author',
    ['overlay.gameStateUnknown']='Unknown',['overlay.gameStateRaceBegin']='Race start',
    ['overlay.gameStateCountingDown']='Countdown',['overlay.gameStateRacing']='Racing',
    ['overlay.gameStatePaused']='Paused',['overlay.gameStateRaceEnd']='Race ended',
    ['overlay.gameStateAdHocRaceBegin']='Racing (joined late)'
}
for key,value in pairs(package.translations or {}) do translations[key]=value end
args.I18NLoader={ResolveByKey=function(key) return translations[key] or key end}
local images={}
for cacheKey,url in pairs(package.imageResources or {}) do
    local key=cacheKey:gsub('^'..tostring(package.id)..':','')
    local metadata=(package.imageMetadata or {})[cacheKey] or {}
    images[key]={CacheKey=cacheKey,Url=url,Width=metadata.width or 0,Height=metadata.height or 0,Dispose=noop}
end
images.ContainsKey=function(key) return images[key]~=nil end
local function canonical(token)
    token=tostring(token):gsub('%.png$',''):match('^%s*(.-)%s*$')
    return (package.pacenoteIconTokenMap or {})[token:lower()] or ''
end
args.Self={ImageResources=images,AnimationTimeSeconds=0,
    GetConfigByKey=function(key) return (package.settings or {})[key] end,
    ResolvePacenoteIconToken=canonical,
    ResolvePacenoteDisplayName=function(token)
        local resolved=canonical(token)
        if resolved=='' then resolved=tostring(token):gsub('%.png$','') end
        return (package.pacenoteDisplayNameMap or {})[resolved] or resolved:gsub('_',' ')
    end}
local context={LocalReplayValid=false,LocalReplay={checkpoints=0,trackLength=0},LocalReplayFinishTime=0,
    GameState=0,LocalReplayDelta=0,LocalReplayComparison={Valid=false},TrackName='',CarName='',CarClass='',
    AudioPackage='',PacenoteType='',ScriptAuthor=''}
args.GameContext=context
args.GameData={}
local best,current={},{}
local function interpolate(samples,target,input,output)
    if #samples==0 then return 0 end
    if target<=samples[1][input] then return samples[1][output] end
    if target>=samples[#samples][input] then return samples[#samples][output] end
    local lo,hi=1,#samples
    while lo<hi do local mid=(lo+hi)//2;if samples[mid][input]<target then lo=mid+1 else hi=mid end end
    local a,b=samples[math.max(1,lo-1)],samples[lo]
    local range=b[input]-a[input]
    if math.abs(range)<0.00001 then return b[output] end
    return a[output]+(b[output]-a[output])*(target-a[input])/range
end
local function refLength() return context.LocalReplay.trackLength or 0 end
local function toReference(d)
    local length=args.GameData.TrackLength or 0
    return length>0 and refLength()>0 and d*refLength()/length or d
end
local function fromReference(d)
    local length=args.GameData.TrackLength or 0
    return length>0 and refLength()>0 and d*length/refLength() or d
end
context.GetBestTimeByDistance={Invoke=function(_,d) return interpolate(best,toReference(d),'Distance','Time') end}
context.GetBestDistanceByTime={Invoke=function(_,t) return fromReference(interpolate(best,t,'Time','Distance')) end}
context.GetTimeByDistance={Invoke=function(_,d) return interpolate(current,d,'Distance','Time') end}
context.GetDeltaByDistanceAndTime={Invoke=function(_,d,t)
    local data=args.GameData
    if math.abs(d-(data.LapDistance or 0))<0.01 and math.abs(t-(data.LapTime or 0))<0.01 then
        return context.LocalReplayComparison.Valid and context.LocalReplayComparison.DeltaAtCurrentProgress or context.LocalReplayDelta
    end
    return t-context.GetBestTimeByDistance:Invoke(d)
end}
context.GetDelta={Invoke=function(_,d)
    if math.abs(d-(args.GameData.LapDistance or 0))<0.01 and context.LocalReplayComparison.Valid then
        return context.LocalReplayComparison.DeltaAtCurrentProgress
    end
    return context.GetTimeByDistance:Invoke(d)-context.GetBestTimeByDistance:Invoke(d)
end}
local signature=''
local bridge={}
function bridge.init() active=dynamic;if onInit then onInit(args) end end
function bridge.update(data,ctx,seconds,clock)
    local nextSignature=table.concat({ctx.TrackName or '',ctx.CarName or '',ctx.CarClass or '',ctx.AudioPackage or '',ctx.PacenoteType or '',ctx.ScriptAuthor or ''},'\0')
    if nextSignature~=signature then dirty=true;signature=nextSignature end
    local trackChanged=ctx.TrackName~=nil and ctx.TrackName~=context.TrackName
    for key,value in pairs(ctx) do
        if not key:match('^Get') then context[key]=value end
    end
    if ctx.BestReplayTimeline then
        best={}
        -- Wire arrays deliberately expose Count and zero-based indices.
        for i=0,ctx.BestReplayTimeline.Count-1 do best[#best+1]=ctx.BestReplayTimeline[i] end
        table.sort(best,function(a,b) return a.Time<b.Time end)
    end
    local time,distance=data.LapTime or 0,data.LapDistance or 0
    local last=current[#current]
    if trackChanged or (last and (time<last.Time or distance<last.Distance-1)) then current={};last=nil end
    if not last or (time>last.Time and distance>=last.Distance and (time-last.Time>=0.05 or distance-last.Distance>=0.5)) then
        current[#current+1]={Time=time,Distance=distance}
        if #current>4096 then
            local compact={};for i=1,#current,2 do compact[#compact+1]=current[i] end
            if compact[#compact]~=current[#current] then compact[#compact+1]=current[#current] end
            current=compact
        end
    end
    args.GameData=data
    data.ToString=function() return data.GameDataText or '' end
    data.GameSpecificDataToString=function() return data.GameSpecificDataText or '' end
    data.BrowserTimeText=clock;args.BrowserTimeText=clock
    args.Self.AnimationTimeSeconds=seconds
    args.WebDashboardStaticLayerDirty=dirty
    if dirty then static={} end
    dynamic={};active=dynamic;commandCount=0
    if onUpdate then onUpdate(args) end
    dirty=false
    return static,dynamic
end
function bridge.shutdown() if onExit then onExit() end end
return bridge
