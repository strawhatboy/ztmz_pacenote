#include "graphics/render_session.hpp"
#include "graphics/async_preparation.hpp"
#include "lua_dashboard.hpp"
#include <algorithm>
#include <atomic>
#include <iostream>
#include <stdexcept>
#include <thread>
using namespace ztmz;
namespace {
void require(bool value,const char* message){if(!value) throw std::runtime_error(message);}
struct TestFrame final:NativeFrame {
    RenderApi selected;
    bool submitted=false;
    explicit TestFrame(RenderApi api):selected(api){}
    RenderApi api() const noexcept override {return selected;}
};
struct Record {
    std::vector<std::string> calls;
    int vertices=0;
    ImGuiContext* context=nullptr;
    bool failInitialize=false;
    bool fontUploaded=false;
    bool busy=false;
    unsigned glassPreparations=0;
};
class TestBackend final:public RenderBackend {
    RenderApi api_;
    Record& record_;
    void record(const char* name){
        require(ImGui::GetCurrentContext()==record_.context,"Backend called with a foreign ImGui context");
        record_.calls.push_back(name);
    }
public:
    TestBackend(RenderApi api,Record& record):api_(api),record_(record){}
    RenderApi api() const noexcept override {return api_;}
    bool readyForFrame() const override {return !record_.busy;}
    void initialize() override {
        record_.context=ImGui::GetCurrentContext();record("initialize");
        if(record_.failInitialize) throw std::runtime_error("Expected initialization failure");
        unsigned char* pixels=nullptr;int w=0,h=0;
        ImGui::GetIO().Fonts->GetTexDataAsRGBA32(&pixels,&w,&h);ImGui::GetIO().Fonts->SetTexID(1);
    }
    void newFrame() override {record("newFrame");}
    void syncImages(const ImageAssets& images) override {record("images");if(images.contains("__ztmz_font_atlas"))record_.fontUploaded=true;}
    Texture texture(const std::string&) const override {return {2,16,16};}
    GlassDraw prepareGlass(NativeFrame&) override {++record_.glassPreparations;return {};}
    void submit(ImDrawData& data,NativeFrame& frame) override {
        record("submit");record_.vertices=data.TotalVtxCount;
        dynamic_cast<TestFrame&>(frame).submitted=true; // API-specific submission output reaches the host.
    }
    void invalidateDeviceObjects() override {record("invalidate");}
    void shutdown() noexcept override {record_.calls.push_back(ImGui::GetCurrentContext()==record_.context?"shutdown":"wrong-context");}
};
void selectionTests() {
    require(parseRenderApi("DX12")==RenderApi::Dx12,"Case-insensitive selection failed");
    bool bad=false;try{parseRenderApi("directx");}catch(const std::invalid_argument&){bad=true;}require(bad,"Unknown API silently accepted");
    RenderApiRouter router;
    const std::vector available{RenderApi::Dx9,RenderApi::Dx11,RenderApi::Vulkan};
    router.configure(RenderApi::Auto,available);
    require(router.active()==RenderApi::Auto,"Auto selected an API before observing a device");
    require(!router.tryActivate(RenderApi::Dx12),"Unavailable API selected");
    std::atomic<int> winners=0;
    std::thread a([&]{if(router.tryActivate(RenderApi::Dx11)) ++winners;});
    std::thread b([&]{if(router.tryActivate(RenderApi::Vulkan)) ++winners;});a.join();b.join();
    require(winners==1,"Multiple graphics APIs acquired the same overlay");
    auto selected=router.active();router.release(selected);
    require(router.active()==RenderApi::Auto,"Device loss did not release selection");
    router.configure(RenderApi::Dx11,available);
    require(!router.tryActivate(RenderApi::Vulkan) && router.tryActivate(RenderApi::Dx11),"Explicit preference ignored");
    bad=false;try{router.configure(RenderApi::Dx12,available);}catch(const std::runtime_error&){bad=true;}
    require(bad && router.active()==RenderApi::Dx11,"Unavailable preference must fail without changing selection");
    for(const auto& entry:backendCatalog()) {
        require(!entry.compiled || entry.implemented,"Planned backend advertised as compiled");
        require(entry.implemented,"A native adapter is missing from the catalog");
    }
}
void sessionTests() {
    // The same actual Lua-generated scene flows through each API contract.
    // TestBackend validates CPU orchestration; it is not a GPU implementation.
    Json package={{"id",0},{"luaScript",R"(
function onUpdate(a)
 a.Graphics.FillCircle(a.Graphics.CreateSolidBrush(20,200,30),a.GameData.Speed,20,10)
end)"}};
    LuaDashboard lua(package,Json::object());
    Scene scene;scene.width=1920;scene.height=1080;scene.layers=lua.update({{"Speed",100}},Json::object(),0,"");
    auto* sentinel=ImGui::CreateContext();
    std::atomic<bool> isolated=false;
    std::thread worker([&]{isolated=ImGui::GetCurrentContext()==nullptr;});worker.join();
    require(isolated&&ImGui::GetCurrentContext()==sentinel,"ImGui context leaked into a worker thread");
    int expectedVertices=0;
    for(auto api:{RenderApi::Dx9,RenderApi::Dx10,RenderApi::Dx11,RenderApi::Dx12,RenderApi::Vulkan}) {
        Record log;
        {
            RenderSession session(std::make_unique<TestBackend>(api,log));
            require(ImGui::GetCurrentContext()==sentinel,"Construction leaked the renderer context");
            TestFrame frame(api);
            session.render({1920,1080,1.f/60},frame,scene,{});
            require(frame.submitted && log.vertices>0,"Draw data did not reach the backend");
            if(!expectedVertices) expectedVertices=log.vertices;
            require(log.vertices==expectedVertices,"Lua geometry changed across backend contracts");
            require(log.calls==std::vector<std::string>{"initialize","images","newFrame","submit"},"Incorrect backend lifecycle order");
            require(log.glassPreparations==0,"Ordinary frame prepared scene capture");
            require(ImGui::GetCurrentContext()==sentinel,"Frame leaked the renderer context");
            const auto count=log.calls.size();
            TestFrame wrong(api==RenderApi::Dx11?RenderApi::Vulkan:RenderApi::Dx11);
            bool rejected=false;try{session.render({1920,1080,0.01f},wrong,scene,{});}catch(const std::invalid_argument&){rejected=true;}
            require(rejected && log.calls.size()==count,"Wrong native frame reached GPU backend");
            session.render({0,0,0.01f},frame,scene,{});
            require(log.calls.size()==count,"Minimized surface submitted a frame");
            log.busy=true;
            session.render({1920,1080,0.01f},frame,scene,{});
            require(!session.lastFrameSubmitted()&&log.calls.size()==count,"Busy GPU was waited on or reported a submitted frame");
            log.busy=false;
            auto withText=scene;
            withText.layers.push_back({"text","dynamic",Json::array({{{"type","drawText"},
                {"fontName","Arial"},{"text","new glyphs"},{"fontSize",18}}})});
            session.render({1920,1080,0.01f},frame,withText,{});
            auto invalidations=std::count(log.calls.begin(),log.calls.end(),"invalidate");
            require(invalidations==0,"New font synchronously invalidated GPU resources");
            auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(10);
            while(!log.fontUploaded&&std::chrono::steady_clock::now()<deadline) {
                session.render({1920,1080,0.01f},frame,withText,{});
                std::this_thread::sleep_for(std::chrono::milliseconds(1));
            }
            require(log.fontUploaded,"Background font atlas never became available");
            require(std::count(log.calls.begin(),log.calls.end(),"invalidate")==0,"Background font adoption invalidated GPU resources");
            session.render({1920,1080,0.01f},frame,withText,{});
            require(std::count(log.calls.begin(),log.calls.end(),"invalidate")==invalidations,
                "Unchanged text invalidated font GPU resources again");
            withText.layers.back().commands[0]["text"]="0123456789 75 FPS 00:25:503";
            session.render({1920,1080,0.01f},frame,withText,{});
            require(std::count(log.calls.begin(),log.calls.end(),"invalidate")==invalidations,
                "Prebuilt numeric glyphs triggered an atlas rebuild");
            session.invalidateDeviceObjects();
            require(log.calls.back()=="invalidate","Reset did not reach backend");
            auto glassScene=scene;
            glassScene.layers[0].commands.push_back(Json{{"type","drawGlassPanel"},{"x",10},{"y",10},{"width",100},{"height",50}});
            session.render({1920,1080,0.01f},frame,glassScene,{});
            require(log.glassPreparations==1,"Glass frame did not prepare optional capture");
            session.render({1920,1080,0.01f},frame,scene,{});
            require(log.glassPreparations==1,"Turning glass off still prepared scene capture");
            bool threw=false;
            try{session.render({1920,1080,0.01f},frame,scene,{},[](const auto&){throw std::runtime_error("UI failed");});}
            catch(const std::runtime_error&){threw=true;}
            require(threw && ImGui::GetCurrentContext()==sentinel,"Failed frame leaked context");
            session.render({1920,1080,0.01f},frame,scene,{}); // ImGui frame lifecycle recovered.
        }
        require(log.calls.back()=="shutdown" && ImGui::GetCurrentContext()==sentinel,"Shutdown did not restore ownership");
    }
    Record failure;failure.failInitialize=true;
    bool threw=false;
    try{RenderSession bad(std::make_unique<TestBackend>(RenderApi::Dx11,failure));}catch(const std::runtime_error&){threw=true;}
    require(threw && failure.calls.back()=="shutdown" && ImGui::GetCurrentContext()==sentinel,"Initialization failure leaked resources/context");
    ImGui::DestroyContext(sentinel);
}
void preparationTests() {
    AsyncPreparation<PreparedContext> job;
    auto* sentinel=ImGui::CreateContext();
    struct Gate{std::atomic<bool> entered=false,proceed=false;};
    auto gate=std::make_shared<Gate>();
    job.start([gate]{gate->entered=true;gate->entered.notify_all();gate->proceed.wait(false);return std::make_unique<PreparedContext>();});
    gate->entered.wait(false);
    require(!job.take(),"Preparing context blocked or published before ready");
    job.cancel(); // cancellation cannot join the blocked worker
    gate->proceed=true;gate->proceed.notify_all();
    job.start([]{return std::make_unique<PreparedContext>();});
    std::unique_ptr<PreparedContext> result;
    auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(5);
    while(!(result=job.take())){require(std::chrono::steady_clock::now()<deadline,"CPU prewarm timeout");std::this_thread::yield();}
    Record record;
    {RenderSession session(std::make_unique<TestBackend>(RenderApi::Dx9,record),std::move(result));}
    require(ImGui::GetCurrentContext()==sentinel,"Prepared context adoption changed caller context");
    job.cancel();job.start([]()->std::unique_ptr<PreparedContext>{throw std::runtime_error("worker failure");});
    bool failed=false;
    while(!failed){try{job.take();}catch(const std::runtime_error&){failed=true;}require(std::chrono::steady_clock::now()<deadline,"Worker error was lost");std::this_thread::yield();}
    job.drain();ImGui::DestroyContext(sentinel);
}
}
int main() {
    try{selectionTests();sessionTests();preparationTests();std::cout<<"Backend selection, API isolation, lifecycle and Lua draw-data checks passed\n";return 0;}
    catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}
}
