#include "dashboard_client.hpp"
#include "renderer.hpp"
#include "font_atlas.hpp"
#include "font_worker.hpp"
#include <thread>
#include <chrono>
#include <cmath>
#include <fstream>
#include <iostream>
#include <stdexcept>
using namespace ztmz;
void require(bool b,const char* text){if(!b) throw std::runtime_error(text);}
Json package(const std::string& source,int id=0) {
    return {{"id",id},{"luaScript",source},{"commonScripts",Json::array()},{"settings",Json::object()}};
}
Json telemetry = {{"Speed",30},{"Gear",3},{"MaxGears",6},{"RPM",5000},{"MaxRPM",8000},
    {"Throttle",0.7},{"Brake",0.2},{"Clutch",0},{"HandBrake",0},{"HandBrakeValid",true},
    {"ShiftLightsRPMValid",false},{"ShiftLightsRPMStart",6000},{"ShiftLightsRPMEnd",7500},
    {"Steering",0.2},{"G_lat",0.3},{"G_long",0.5},{"LapDistance",1000},{"LapTime",30},
    {"TrackLength",5000},{"CompletionRate",0.2},{"Time",30},
    {"CarDamage",{{"Dashboard",{{"IsAvailable",false}}}}}};
Json context = {{"TrackName","Test stage"},{"CarName","Test car"},{"CarClass","Rally"},
    {"AudioPackage","test"},{"ScriptAuthor","test"},{"PacenoteType","test"},{"GameState",3},
    {"LocalReplayValid",true},{"LocalReplayDelta",2},{"LocalReplayFinishTime",150},
    {"LocalReplay",{{"checkpoints",5},{"trackLength",5000}}},
    {"LocalReplayComparison",{{"Valid",true},{"DeltaAtCurrentProgress",2},{"CurrentCompletionRate",0.2},
        {"BestTimeAtCurrentProgress",28},{"BestCompletionRateAtCurrentTime",0.21},{"BestDistanceAtCurrentTime",1050}}},
    {"BestReplayTimeline",Json::array({{{"Time",0},{"Distance",0}},{{"Time",140},{"Distance",5000}}})},
    {"CurrentPacenote",{{"GroupId","test-1"},{"DispatchSequence",1},{"Tokens",Json::array({
        {{"Token","3_left"},{"Modifiers",Json::array({"long"})}}})}}}};
Json config = {{"HudChromaKeyMode",false},{"HudShiftGreenZone",0.5},{"HudShiftYellowZone",0.7},
    {"HudShiftRedZone",0.9},{"FrameRate",60},{"CanvasWidth",1280},{"CanvasHeight",720}};
void runtimeTests() {
    auto p = package(R"(
local ticks=0
function onInit(a)
 assert(os==nil and io==nil and package==nil and debug==nil and require==nil and load==nil and coroutine==nil and pcall==nil)
 assert(a.Graphics.Width==1920)
end
function onUpdate(a)
 ticks=ticks+1
 local g=a.Graphics;local b=g.CreateSolidBrush(255,0,0,128)
 g.BeginStaticLayer()
 if a.WebDashboardStaticLayerDirty then g.FillRectangle(b,0,0,10,10) end
 g.EndStaticLayer()
 g.DrawText(g.CreateFont('Arial',20),b,1,2,tostring(ticks)..':'..tostring(a.GameData.Speed))
 assert(a.GameContext.CurrentPacenote.Tokens.Count==1)
 assert(a.GameContext.CurrentPacenote.Tokens[0].Token=='3_left')
 assert(math.abs(a.GameContext.GetBestTimeByDistance:Invoke(2500)-70)<0.001)
end
function onExit() assert(true) end
)");
    LuaDashboard a(p,config), b(p,config);
    auto first=a.update(telemetry,context,1,"12:00:00");
    require(first[0].commands.size()==1,"Static drawing missing");
    require(first[1].commands[0]["text"]=="1:30","Lua did not read telemetry");
    auto next=a.update(telemetry,context,2,"12:00:01");
    require(next[0].commands==first[0].commands,"Static layer was not retained");
    require(next[1].commands[0]["text"]=="2:30","Lua state did not persist");
    require(b.update(telemetry,context,2,"12:00:01")[1].commands[0]["text"]=="1:30","Lua VMs share globals");
    auto changed=context;changed["TrackName"]="New stage";
    require(a.update(telemetry,changed,3,"12:00:02")[0].commands.size()==1,"Static refresh appended old commands");
    a.shutdown();a.shutdown();
    bool exitCalled=false;
    try {LuaDashboard exiting(package("function onExit() error('exit reached') end"),config);exiting.shutdown();}
    catch(const std::exception& e){exitCalled=std::string(e.what()).find("exit reached")!=std::string::npos;}
    require(exitCalled,"onExit was not invoked");
    for (const auto& source : {"while true do end", "local x=string.rep('x',8*1024*1024)", "function onInit() error('expected init error') end"}) {
        bool rejected=false;
        try { LuaDashboard bad(package(source),config,{1024*1024,10000}); }
        catch(const std::exception&){rejected=true;}
        require(rejected,"Lua limits/errors not enforced");
    }
    bool failed=false;
    try {LuaDashboard bad(package("function onUpdate() while true do end end"),config,{1024*1024,10000});bad.update(telemetry,context,0,"");}
    catch(const std::exception&){failed=true;}
    require(failed,"Infinite update did not terminate");
    int loads=0;
    DashboardClient client([&](int id){++loads;return package("function onUpdate(a) a.Graphics.FillCircle(a.Graphics.CreateSolidBrush(0,255,0),a.GameData.Speed,1,2) end",id);});
    auto message=Json{{"type","browserLuaTelemetry"},{"sceneRevision",1},{"dashboards",Json::array({{{"id",0},{"order",0},{"configurationRevision",1}}})},
        {"config",config},{"data",telemetry},{"context",context}};
    auto result=client.accept(message,0,"");
    require(result.changed && result.controlMessage.empty() && loads==1,"Lua scene did not load");
    require(client.scene().layers[1].commands[0]["x"]==30,"Manager did not execute Lua");
    client.accept(message,1,"");require(loads==1,"Unchanged package reloaded");
    message.erase("dashboards");message["data"]["Speed"]=50;
    client.accept(message,2,"");require(client.scene().layers[1].commands[0]["x"]==50,"Telemetry delta not applied");
    message["sceneRevision"]=2;
    require(client.accept(message,3,"").controlMessage.find("requestBrowserLuaScene")!=std::string::npos,"Missing scene recovery");
    DashboardClient broken([](int id){return package("function onUpdate() error('broken') end",id);});
    message["dashboards"]=Json::array({{{"id",0}}});
    require(broken.accept(message,4,"").controlMessage.empty(),"Individual Lua failure requested client-wide fallback");
    require(broken.status().find("Dashboard 0 suspended")!=std::string::npos,"Failed dashboard diagnostic missing");
    require(broken.scene().layers.empty(),"Failed Lua left stale drawing");
    // Scene changes must reload changed packages, preserve unchanged VMs, and
    // interleave compatibility layers with local Lua in dashboard order.
    message["dashboards"]=Json::array({{{"id",0},{"order",2},{"configurationRevision",2}}});
    message["hybrid"]=true;
    client.accept(message,5,"");require(loads==2,"Configuration revision did not reload package");
    auto compatibility=Json::parse(R"({"type":"layeredDrawCommands","sceneRevision":10,"browserLuaDashboardIds":[0],"layers":[{"id":"dashboard-1:dynamic:0","kind":"dynamic","commands":[{"type":"fillCircle","x":1}]},{"id":"dashboard-3:dynamic:0","kind":"dynamic","commands":[{"type":"fillCircle","x":3}]}]})");
    client.accept(compatibility,6,"");
    require(client.scene().layers.size()==4,"Hybrid layers were dropped");
    require(client.scene().layers.front().id=="dashboard-1:dynamic:0" && client.scene().layers.back().id=="dashboard-3:dynamic:0","Hybrid z-order is wrong");
    message["dashboards"][0]["order"]=0;message["sceneRevision"]=3;
    client.accept(message,7,"");require(loads==2,"Reordering needlessly reinitialized Lua");
    require(client.scene().layers.front().id=="native-lua-0-static","Reordering failed");
    message["dashboards"]=Json::array();message["hybrid"]=false;message["sceneRevision"]=4;
    client.accept(message,8,"");require(client.scene().layers.empty(),"Removed dashboard left stale drawing");
    client.reset();require(client.scene().layers.empty(),"Disconnect left stale drawing");
}
struct HeadlessImGui {
    HeadlessImGui() {
        ImGui::CreateContext(); auto& io=ImGui::GetIO();io.IniFilename=nullptr;
        io.DisplaySize={1920,1080};io.DeltaTime=1.f/60;
        unsigned char* pixels=nullptr;int width=0,height=0;
        io.Fonts->GetTexDataAsRGBA32(&pixels,&width,&height);io.Fonts->SetTexID(1);
    }
    ~HeadlessImGui(){ImGui::DestroyContext();}
};
void render(const std::vector<Layer>& layers) {
    Scene scene;scene.width=1920;scene.height=1080;scene.layers=layers;
    ImGui::NewFrame();
    auto warnings=draw(scene,[](const std::string&){return Texture{2,256,256};});
    warnings.erase("font family/style and exact text metrics");
    require(warnings.empty(),"Native renderer rejected a built-in Lua drawing command");
    ImGui::Render();
    require(ImGui::GetDrawData()->TotalVtxCount>0,"Native renderer produced no vertices");
}
int main(int argc,char** argv) {
    HeadlessImGui imgui;
    try {
        if(argc>1) {
            std::ifstream input(argv[1]);Json p;input>>p;
            LuaDashboard runtime(p,config);
            if(p.value("testName",std::string{})=="car_damage") {
                auto& damage=telemetry["CarDamage"]["Dashboard"];
                damage["IsAvailable"]=true;
                for(const auto* part:{"FrontBumper","RearBumper","Radiator","Engine","Differential","LeftDoor","RightDoor",
                    "FrontLeftWheel","FrontRightWheel","RearLeftWheel","RearRightWheel",
                    "FrontLeftBrake","FrontRightBrake","RearLeftBrake","RearRightBrake",
                    "FrontLeftSuspension","FrontRightSuspension","RearLeftSuspension","RearRightSuspension",
                    "FrontLeftTyre","FrontRightTyre","RearLeftTyre","RearRightTyre"}) damage[std::string(part)+"Severity"]=1;
            }
            std::size_t commands=0;
            const auto name=p.value("testName",std::string{});
            if(name=="modern_wrc" || name=="modern_wrc_2" || name=="modern_wrc_2025" || name=="modern_wrc_2025v2") {
                auto hasText=[](const std::vector<Layer>& layers,const std::string& text) {
                    for(const auto& layer:layers) for(const auto& command:layer.commands)
                        if(command.value("text",std::string{}).find(text)!=std::string::npos) return true;
                    return false;
                };
                auto data=telemetry;
                data["Hybrid"]={{"BatteryChargeFraction",0.8},{"BoostProgressFraction",0.5},{"BoostMap",2.0},
                    {"BoostActive",true},{"BoostReady",false},{"RegenerationActive",false}};
                for(const auto* state:{"BOOST","READY","REGEN","STANDBY"}) {
                    data["Hybrid"]["BoostActive"]=std::string(state)=="BOOST";
                    data["Hybrid"]["BoostReady"]=std::string(state)=="READY";
                    data["Hybrid"]["RegenerationActive"]=std::string(state)=="REGEN";
                    auto layers=runtime.update(data,context,0,"12:34:56");render(layers);
                    require(hasText(layers,"80%") && (name=="modern_wrc_2" ? !hasText(layers,state) : hasText(layers,state)) && hasText(layers,"M2"),"Hybrid readings missing/wrong");
                    require(!hasText(layers,"M2.0"),"Hybrid map has a fractional suffix");
                }
                data["Hybrid"].erase("BatteryChargeFraction");
                require(hasText(runtime.update(data,context,0,""),"--"),"Missing battery was invented");
                data.erase("Hybrid");
                require(!hasText(runtime.update(data,context,0,""),"M2"),"Hybrid remained after changing car");
                auto disabled=p;disabled["settings"]["dashboards.settings.showHybrid"]=false;
                LuaDashboard hidden(disabled,config);data["Hybrid"]={{"BatteryChargeFraction",0.8}};
                require(!hasText(hidden.update(data,context,0,""),"80%"),"Hybrid setting ignored");
            }
            if(name=="classic_dashboard" || name=="modern_wrc_2025" || name=="modern_wrc_2025v2" || name=="liquid_glass") {
                auto fingerprint=[](const std::vector<Layer>& layers) {
                    std::string result;
                    for(const auto& layer:layers) for(const auto& command:layer.commands) {
                        require(command.value("text",std::string{})!="FFB","Unwanted FFB label");
                        result+=command.dump();
                    }
                    return result;
                };
                auto enabled=p;
                enabled["settings"]["dashboards.settings.showSteering"]=true;
                enabled["settings"]["dashboards.settings.showSteeringIndicator"]=true;
                LuaDashboard ffb(enabled,config);
                const auto withoutFfb=fingerprint(ffb.update(telemetry,context,0,""));
                for(double value:{-1.,-.4,0.,.6,1.}) {
                    auto data=telemetry;data["ForceFeedback"]={{"OutputFraction",value}};
                    auto layers=ffb.update(data,context,0,"");render(layers);
                    require(fingerprint(layers)!=withoutFfb,"Valid signed/zero FFB missing from steering dashboard");
                }
                for(auto missing:{Json(),Json::object(),Json{{"OutputFraction",1.01}},Json{{"OutputFraction",-1.01}}}) {
                    auto data=telemetry;data["ForceFeedback"]=missing;
                    auto layers=ffb.update(data,context,0,"");render(layers);
                    require(fingerprint(layers)==withoutFfb,"Unavailable/invalid FFB retained its indicator");
                }
                if(name!="liquid_glass") {
                    enabled["settings"]["dashboards.settings.showSteering"]=false;
                    enabled["settings"]["dashboards.settings.showSteeringIndicator"]=false;
                    LuaDashboard hidden(enabled,config);auto data=telemetry;
                    data["ForceFeedback"]={{"OutputFraction",.5}};
                    require(fingerprint(hidden.update(data,context,0,""))==fingerprint(hidden.update(telemetry,context,0,"")),"FFB ignored steering visibility setting");
                }
            }
            if(p.value("testName",std::string{})=="modern_wrc_2025v2") {
                // A viewer may connect before the first usable engine telemetry.
                for(const auto& values: {std::pair{0,0},std::pair{5000,0},std::pair{5000,-1},std::pair{9000,8000}}) {
                    auto data=telemetry;data["RPM"]=values.first;data["MaxRPM"]=values.second;
                    render(runtime.update(data,context,0,"12:34:56"));
                }
                auto missing=telemetry;missing.erase("MaxRPM");
                render(runtime.update(missing,context,0,"12:34:56"));
            }
            for(int frame=0;frame<4;++frame) {
                auto data=telemetry;data["Speed"]=30+frame*5;data["LapTime"]=30+frame;data["LapDistance"]=1000+frame*30;
                auto layers=runtime.update(data,context,frame*0.1,"12:34:56");
                for(const auto& layer:layers) commands+=layer.commands.size();
                render(layers);
            }
#ifdef _WIN32
            if(name=="modern_wrc_2025" || name=="modern_wrc_2025v2" || name=="mapInfo") {
                // Exercise the production worker with real dashboard font sizes,
                // not just geometry rendered through ImGui's default font.
                FontWorker worker;
                Scene scene;scene.width=1920;scene.height=1080;
                scene.layers=runtime.update(telemetry,context,1,"12:34:56");
                for(float scale:{1.f,0.75f}) {
                    worker.request(scene,scale);
                    std::shared_ptr<PreparedFonts> prepared;
                    auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(5);
                    while(!(prepared=worker.takeReady()) && std::chrono::steady_clock::now()<deadline)
                        std::this_thread::sleep_for(std::chrono::milliseconds(1));
                    require(prepared!=nullptr,"WRC 2025 font worker did not produce an atlas");
                    require(prepared->image && !prepared->image->rgba.empty(),"WRC font texture missing");
                    require(!prepared->atlas.TexPixelsAlpha8 && !prepared->atlas.TexPixelsRGBA32,
                        "Worker retained redundant CPU atlas bitmaps");
                    prepared->atlas.SetTexID(1);
                    prepared->fonts->setScale(scale);
                    if(name=="mapInfo") {
                        for(const auto& layer:scene.layers)for(const auto& c:layer.commands) {
                            if(c.value("type",std::string{})!="drawTextWithBackground")continue;
                            const auto text=c.at("text").get<std::string>();
                            for(const auto* field:{"TrackName","CarName","CarClass","AudioPackage","ScriptAuthor"})
                                require(text.find(context.at(field).get<std::string>())!=std::string::npos,"Map info dropped a context value");
                            require(std::count(text.begin(),text.end(),'\n')==6,"Map info dropped a row");
                            std::set<std::string> fontWarnings;
                            auto* font=prepared->fonts->lookup(c,fontWarnings);
                            const float size=c.at("fontSize").get<float>()*scale;
                            auto actual=font->CalcTextSizeA(size,10000,0,text.c_str());
                            require(std::abs(actual.x-c.at("textWidth").get<float>()*scale)<1.1f,"Map info measured width differs from rendered width");
                            require(c.at("x").get<float>()*scale+actual.x<=scene.width*scale+1,"Map info values extend off the right edge");
                            // Compare actual renderer geometry to the measured
                            // font size, away from clipping so dropped glyphs
                            // cannot conceal a width mismatch.
                            auto command=c;command["type"]="drawText";command["x"]=16;command["y"]=16;
                            Scene sample=scene;sample.layers={{"text","dynamic",Json::array({command})}};
                            const auto display=ImGui::GetIO().DisplaySize;
                            ImGui::GetIO().DisplaySize={scene.width*scale,scene.height*scale};
                            auto vertices=[] {
                                std::vector<ImVec2> positions;
                                for(const auto* list:ImGui::GetDrawData()->CmdLists)
                                    for(const auto& vertex:list->VtxBuffer)positions.push_back(vertex.pos);
                                return positions;
                            };
                            ImGui::NewFrame();draw(sample,{},[&](const Json&,std::set<std::string>&){return font;});ImGui::Render();
                            auto rendered=vertices();
                            ImGui::NewFrame();auto* dl=ImGui::GetBackgroundDrawList();dl->PushTextureID(font->ContainerAtlas->TexID);
                            dl->AddText(font,size,{std::round(16*scale),std::round(16*scale)},IM_COL32_WHITE,text.c_str());
                            dl->PopTextureID();ImGui::Render();auto expected=vertices();
                            ImGui::GetIO().DisplaySize=display;
                            require(rendered.size()==expected.size(),"Scaled map text lost glyph geometry");
                            for(std::size_t i=0;i<rendered.size();++i)
                                require(std::abs(rendered[i].x-expected[i].x)<0.01f && std::abs(rendered[i].y-expected[i].y)<0.01f,
                                    "Renderer font size differs from measured layout at fractional scale");
                        }
                    }
                    ImGui::NewFrame();
                    auto warnings=draw(scene,[](const std::string&){return Texture{2,256,256};},
                        [&](const Json& c,std::set<std::string>& w){return prepared->fonts->lookup(c,w);});
                    ImGui::Render();
                    require(warnings.empty(),"WRC dashboard rejected production fonts");
                    require(ImGui::GetDrawData()->TotalVtxCount>0,"WRC dashboard drew no vertices");
                }
            }
#endif
            require(commands>0,"Built-in dashboard emitted no commands");runtime.shutdown();
            std::cout<<p.value("testName",std::string("dashboard"))<<": "<<commands<<" commands\n";
        } else {
            runtimeTests();
#ifdef _WIN32
            {
                auto roman=selectFont("WRC Clean Roman",400,false),bold=selectFont("WRC Clean",700,false);
                require(!roman.substituted && !bold.substituted,"Bundled WRC fonts were not resolved");
                require(roman.faces.front()->identity!=bold.faces.front()->identity,"Bold WRC font was not selected");
                auto legacyRoman=selectFont("WRC Clean Roman",400,false),legacyBold=selectFont("WRC Clean Bold",400,false);
                require(!legacyRoman.substituted && legacyRoman.faces.front()==roman.faces.front(),"Legacy WRC Roman alias lost bundled font");
                require(!legacyBold.substituted && legacyBold.faces.front()==bold.faces.front(),"Legacy WRC Bold alias lost bundled font");
                require(roman.faces.front()->identity.find("WRCCleanRoman.ttf")!=std::string::npos,"Roman family resolved to wrong font file");
                require(bold.faces.front()->identity.find("WRCCleanBold.ttf")!=std::string::npos,"Bold family resolved to wrong font file");
                for(const auto* family:{"WRC Clean","WRC Clean Bold"})for(int weight:{400,700}) {
                    auto face=selectFont(family,weight,false);
                    require(!face.substituted && face.faces.front()==bold.faces.front(),"WRC bold family changed weight or fell back to a system font");
                }
                require(selectFont("WRC Clean Roman",700,false).faces.front()==bold.faces.front(),"Bold Roman alias disagrees with browser font mapping");
                Scene textScene;textScene.width=1920;textScene.height=1080;
                Json command={{"type","drawText"},{"fontName","Consolas"},{"fontSize",24},
                    {"fontWeight",400},{"text",std::string("RPM ")+"\xe5\xb7\xa6\xe5\x85\xad"},{"x",0},{"y",0}};
                textScene.layers.push_back({"fonts","dynamic",Json::array({command})});
                DashboardFonts fonts;require(fonts.collect(textScene),"Initial font atlas not requested");fonts.rebuild();
                ImGui::GetIO().Fonts->SetTexID(1);
                std::set<std::string> warnings;auto* font=fonts.lookup(command,warnings);
                require(font && font->FindGlyphNoFallback(0x5de6) && font->FindGlyphNoFallback(0x516d),"System CJK fallback missing");
                require(warnings.empty(),"Unexpected font substitution");
                const auto text=command["text"].get<std::string>();
                auto measured=measureText(selectFont("Consolas",400,false),24,text);
                auto rendered=font->CalcTextSizeA(24,10000,0,text.c_str());
                require(std::abs(measured.width-rendered.x)<1.1f,"Lua and ImGui text widths disagree");
                for(const auto* tabbed:{"Track:\tTest stage", "Car:\tFord\r\nAuthor:\tTest author", "\t\tRPM 5000"}) {
                    auto tabMeasured=measureText(selectFont("Consolas",400,false),24,tabbed);
                    auto tabRendered=font->CalcTextSizeA(24,10000,0,tabbed);
                    std::cout<<"Tabbed text: measured="<<tabMeasured.width<<", rendered="<<tabRendered.x<<'\n';
                    require(std::abs(tabMeasured.width-tabRendered.x)<1.1f,"Tab measurement clips right-aligned text");
                }
                require(!fonts.collect(textScene),"Unchanged text rebuilt font atlas");
                require(font->FontSize==24,"Font not rasterized at requested pixel size");
                require(fonts.collect(textScene,0.5f),"Smaller viewport did not request a smaller font");
                fonts.rebuild();
                require(fonts.lookup(command,warnings)->FontSize==12,"Small font is still a scaled large atlas glyph");
                require(!fonts.collect(textScene,0.501f),"Subpixel size variation rebuilt the atlas");
                require(!fonts.collect(textScene,1.f),"Returning to a cached size rebuilt the atlas");
                require(fonts.lookup(command,warnings)->FontSize==24,"Cached size not restored");
                ImGui::GetIO().Fonts->SetTexID(1);
                ImGui::NewFrame();
                require(draw(textScene,{},[&](const Json& c,std::set<std::string>& w){return fonts.lookup(c,w);}).empty(),"Font-aware rendering reported unsupported text");
                ImGui::Render();
            }
#endif
            Scene scene;scene.width=1920;scene.height=1080;
            scene.layers.push_back({"compatibility", "dynamic", Json::parse(R"([
                {"type":"clear","color":null},
                {"type":"setClip","x":10,"y":20,"width":100,"height":80},
                {"type":"fillGeometry","points":[0,0,200,0,200,200,0,200],"color":4294967295},
                {"type":"releaseClip"},
                {"type":"drawGeometry","points":[0,0,20,20],"color":4294967295}
            ])")});
            ImGui::NewFrame();
            require(draw(scene,{}).empty(),"Compatibility geometry/clip commands rejected");
            ImGui::Render();
            bool clipped=false,unclipped=false;
            const auto* draws=ImGui::GetDrawData();
            for(int i=0;i<draws->CmdListsCount;++i) for(const auto& cmd:draws->CmdLists[i]->CmdBuffer) {
                if(!cmd.ElemCount) continue;
                if(cmd.ClipRect.x==10 && cmd.ClipRect.y==20 && cmd.ClipRect.z==110 && cmd.ClipRect.w==100) clipped=true;
                if(cmd.ClipRect.x==0 && cmd.ClipRect.y==0 && cmd.ClipRect.z==1920 && cmd.ClipRect.w==1080) unclipped=true;
            }
            require(clipped && unclipped,"Compatibility clip was not applied and released");
            scene.layers[0].commands=Json::parse(R"([{"type":"fillGeometry","points":[1,2,3]}])");
            ImGui::NewFrame();auto warnings=draw(scene,{});ImGui::Render();
            require(warnings.size()==1 && warnings.begin()->find("compatibility / fillGeometry")!=std::string::npos,
                "Malformed command diagnostic lacks layer/type");
            std::cout<<"Lua lifecycle, isolation, limits, telemetry and compatibility rendering checks passed\n";
        }
        return 0;
    } catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}
}
