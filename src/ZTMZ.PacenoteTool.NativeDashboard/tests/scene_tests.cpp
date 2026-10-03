#include "scene.hpp"
#include <iostream>
#include <stdexcept>
using namespace ztmz;
void require(bool condition,const char* message){if(!condition)throw std::runtime_error(message);}
int main(){try {
    Scene scene;
    auto snapshot=Json::parse(R"({"type":"layeredDrawCommands","sceneRevision":2,"canvasWidth":1920,"canvasHeight":1080,"layers":[{"id":"s","kind":"static","commands":[{"type":"fillCircle"}]},{"id":"d","kind":"dynamic","commands":[{"type":"drawText","text":"old"}]}]})");
    require(scene.apply(snapshot),"Snapshot rejected");
    require(scene.width==1920 && scene.layers.size()==2,"Snapshot dimensions/order");
    require(scene.apply(Json::parse(R"({"type":"layeredDrawCommands","sceneRevision":2,"dynamicLayers":[{"id":"d","commands":[]}]})")),"Delta rejected");
    require(scene.layers[0].commands.size()==1 && scene.layers[1].commands.empty(),"Static retention/dynamic clearing");
    require(!scene.apply(Json::parse(R"({"type":"layeredDrawCommands","sceneRevision":3,"dynamicLayers":[]})")),"Unknown revision must request snapshot");
    require(scene.revision==2,"Rejected delta mutated scene");
    auto old=snapshot;old["sceneRevision"]=1;scene.apply(old);
    require(scene.layers[1].commands.empty(),"Stale snapshot replaced current state");
    auto invalid=snapshot;invalid["canvasWidth"]=0;
    bool threw=false;try{scene.apply(invalid);}catch(const std::exception&){threw=true;}
    require(threw && scene.width==1920,"Invalid frame must be atomic");
    invalid=snapshot;invalid["layers"][1]["id"]="s";
    threw=false;try{scene.apply(invalid);}catch(const std::exception&){threw=true;}
    require(threw && scene.layers[1].id=="d","Duplicate layer must be atomic");
    require(!scene.apply(Json::parse(R"({"type":"layeredDrawCommands","sceneRevision":2,"dynamicLayers":[{"id":"s","commands":[]}]})")),"Static layer cannot be patched as dynamic");
    scene.apply(Json::parse(R"({"type":"drawCommands","commands":[]})"));
    require(!scene.layered && scene.layers.size()==1 && scene.layers[0].commands.empty(),"Legacy switch clears retained state");
    scene=Scene{};
    require(!scene.apply(Json::parse(R"({"type":"layeredDrawCommands","sceneRevision":2,"dynamicLayers":[]})")),"Reconnect needs snapshot");
    std::cout<<"Scene protocol checks passed\n";return 0;
} catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}}
