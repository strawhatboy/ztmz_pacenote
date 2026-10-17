#include "dashboard_client.hpp"
#include <iostream>
#include <stdexcept>
using namespace ztmz;
namespace {
void require(bool value,const char* reason){if(!value)throw std::runtime_error(reason);}
Json package(int id,const std::string& script){return {{"id",id},{"luaScript",script},{"settings",Json::object()}};}
const std::string healthy=R"(
local ticks=0
function onUpdate(a)
 ticks=ticks+1
 a.Graphics.FillCircle(a.Graphics.CreateSolidBrush(255,255,255),a.GameData.Speed,ticks,2)
end
)";
const std::string intermittent=R"(
local ticks=0
function onUpdate(a)
 ticks=ticks+1
 local g=a.Graphics; local b=g.CreateSolidBrush(255,255,255)
 g.BeginStaticLayer()
 if a.WebDashboardStaticLayerDirty then g.FillCircle(b,777,1,2) end
 g.EndStaticLayer()
 g.FillCircle(b,a.GameData.Bad and 0/0 or a.GameData.Speed,ticks,2)
end
)";
Json scene(){return {{"type","browserLuaTelemetry"},{"sceneRevision",1},
    {"dashboards",Json::array({{{"id",0},{"order",0}},{{"id",12},{"order",2}},{{"id",13},{"order",3}}})},
    {"data",{{"Speed",30},{"Bad",false}}},{"context",Json::object()},{"config",Json::object()}};}
const Layer* layer(const DashboardClient& client,int id,const char* kind="dynamic"){
    for(const auto& layer:client.scene().layers)if(layer.id=="native-lua-"+std::to_string(id)+"-"+kind)return &layer;
    return nullptr;
}
void isolationAndRecovery(){
    std::map<int,int> loads;
    DashboardClient client([&](int id){++loads[id];return package(id,id==12?intermittent:healthy);});
    auto message=scene();require(client.accept(message,0,"").controlMessage.empty(),"Initial scene failed");
    require(layer(client,12,"static")&&layer(client,12,"static")->commands.size()==1,"Initial static layer missing");
    message.erase("dashboards");message["data"]={{"Speed",50},{"Bad",true}};
    auto failure=client.accept(message,0.1,"");
    require(failure.changed&&failure.controlMessage.empty(),"One invalid coordinate triggered global fallback");
    require(!layer(client,12)&&!layer(client,12,"static"),"Failed dashboard retained stale static/dynamic layers");
    for(int id:{0,13})require(layer(client,id)&&layer(client,id)->commands[0]["x"]==50&&layer(client,id)->commands[0]["y"]==2,
        "Failure interrupted a healthy dashboard before/after it in order");
    require(client.status().find("Dashboard 12 suspended")!=std::string::npos&&client.status().find("Non-finite drawing coordinate")!=std::string::npos,
        "Failure diagnostic lost dashboard identity or original reason");
    message["data"]["Bad"]=false;client.accept(message,0.2,"");
    require(!layer(client,12),"Failed dashboard retried every telemetry tick");
    require(layer(client,13)->commands[0]["y"]==3,"Healthy dashboard stopped during retry delay");
    auto recovered=client.accept(message,1.1,"");
    require(recovered.controlMessage.empty()&&layer(client,12)&&layer(client,12)->commands[0]["y"]==1,"Transient failure did not recover with a fresh VM");
    require(layer(client,12,"static")->commands.size()==1,"Recovered VM lost static content");
    require(layer(client,0)->commands[0]["y"]==4&&layer(client,13)->commands[0]["y"]==4,"Recovery restarted healthy VMs");
    require(loads[0]==1&&loads[12]==1&&loads[13]==1,"Runtime recovery downloaded packages again");
    require(client.status().find("suspended")==std::string::npos,"Recovery left a stale failure diagnostic");
}
void loadBackoffAndRemoval(){
    int attempts=0;bool available=false;
    DashboardClient client([&](int id){if(id==12){++attempts;if(!available)throw std::runtime_error("package temporarily unavailable");}return package(id,healthy);});
    auto message=scene();client.accept(message,0,"");
    require(layer(client,0)&&layer(client,13)&&!layer(client,12),"Package error affected healthy dashboards");
    // Repeated full scenes with unchanged descriptors must not bypass backoff.
    for(double time:{0.1,0.2,0.9})client.accept(message,time,"");require(attempts==1,"Package load retried without delay");
    client.accept(message,1,"");require(attempts==2,"First retry absent");
    client.accept(message,2,"");require(attempts==2,"Backoff did not increase");
    for(double time:{3.,7.,15.,31.,61.})client.accept(message,time,"");
    require(attempts==7,"Retry backoff did not cap at 30 seconds");
    client.accept(message,90.9,"");require(attempts==7,"Retry ran before deadline");
    available=true;client.accept(message,91,"");require(attempts==8&&layer(client,12),"Package download failure did not recover");
    // Reset must erase cooldown, diagnostics and all retained VMs/source.
    client.reset();require(client.scene().layers.empty(),"Reset retained content");
    client.accept(message,91.1,"");require(attempts==9,"Reset failed to reload package");
    message["dashboards"].erase(1);message["sceneRevision"]=2;
    client.accept(message,91.2,"");require(!layer(client,12)&&layer(client,13),"Dashboard removal broke surviving layers");
}
void initFailureHybridAndRevision(){
    bool broken=true;std::map<int,int> loads;
    DashboardClient client([&](int id){++loads[id];return package(id,id==12&&broken?"function onInit() error('bad init') end":healthy);});
    auto compatibility=Json::parse(R"({"type":"layeredDrawCommands","sceneRevision":1,"browserLuaDashboardIds":[0,12,13],"layers":[{"id":"dashboard-1:dynamic:0","kind":"dynamic","commands":[{"type":"fillCircle","x":123}]}]})");
    client.accept(compatibility,0,"");auto message=scene();message["hybrid"]=true;
    require(client.accept(message,0,"").controlMessage.empty(),"Initialization failure disabled whole native client");
    require(client.scene().layers.size()==5&&client.scene().layers[2].id=="dashboard-1:dynamic:0","Failure removed/reordered compatibility layers");
    require(client.status().find("bad init")!=std::string::npos,"Initialization error was hidden");
    broken=false;message["sceneRevision"]=2;message["dashboards"][1]["configurationRevision"]=2;
    client.accept(message,0.1,"");
    require(layer(client,12)&&loads[12]==2&&loads[0]==1&&loads[13]==1,"Configuration revision did not immediately recover only the changed dashboard");
    require(client.scene().layers.size()==7,"Recovery lost compatibility composition");
    // Protocol errors still use the existing explicit compatibility fallback.
    message["dashboards"][1]["id"]=0;
    auto invalid=client.accept(message,0.2,"");
    require(invalid.controlMessage.find("browserLuaFallback")!=std::string::npos&&client.scene().layers.empty(),"Malformed scene lost protocol fallback");
}
}
int main(){try{isolationAndRecovery();loadBackoffAndRemoval();initFailureHybridAndRevision();
    std::cout<<"Dashboard failure isolation, stale-layer clearing, cached VM recovery, bounded retries, hybrid composition and revision recovery passed\n";return 0;
}catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}}
