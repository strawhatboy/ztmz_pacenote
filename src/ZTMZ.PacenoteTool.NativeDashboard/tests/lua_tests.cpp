#include "dashboard_client.hpp"
#include "renderer.hpp"
#include "font_atlas.hpp"
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
    require(broken.accept(message,4,"").controlMessage.find("browserLuaFallback")!=std::string::npos,"Failed Lua did not request fallback");
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
            require(commands>0,"Built-in dashboard emitted no commands");runtime.shutdown();
            std::cout<<p.value("testName",std::string("dashboard"))<<": "<<commands<<" commands\n";
        } else {
            runtimeTests();
#ifdef _WIN32
            {
                auto roman=selectFont("WRC Clean",400,false),bold=selectFont("WRC Clean",700,false);
                require(!roman.substituted && !bold.substituted,"Bundled WRC fonts were not resolved");
                require(roman.faces.front()->identity!=bold.faces.front()->identity,"Bold WRC font was not selected");
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
                require(!fonts.collect(textScene),"Unchanged text rebuilt font atlas");
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
