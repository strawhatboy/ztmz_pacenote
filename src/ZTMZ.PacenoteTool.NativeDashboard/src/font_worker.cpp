#include "font_worker.hpp"
#include <condition_variable>
#include <deque>
#include <functional>
#include <thread>
#include <cmath>
#include <algorithm>
#include <utility>
namespace ztmz {
namespace {
// Process lifetime: dropping a graphics session never joins a rasterizing worker
// on the game's Present/Resize thread. Jobs only reference CPU-owned state.
struct Executor {
    std::mutex mutex;std::condition_variable wake;
    std::deque<std::function<void()>> jobs;
    Executor(){std::thread([this]{for(;;){std::function<void()> job;
        {std::unique_lock lock(mutex);wake.wait(lock,[&]{return !jobs.empty();});job=std::move(jobs.front());jobs.pop_front();}
        try{job();}catch(...){}
    }}).detach();}
    void post(std::function<void()> job){{std::lock_guard lock(mutex);jobs.push_back(std::move(job));}wake.notify_one();}
};
Executor& executor(){static auto* value=new Executor;return *value;}
}
struct FontWorker::State {
    std::mutex mutex;Scene pending;bool queued=false;
    std::unique_ptr<DashboardFonts> desired;
    std::shared_ptr<PreparedFonts> ready;
    static void process(std::weak_ptr<State> weak);
};
FontWorker::FontWorker():state_(std::make_shared<State>()){}
void FontWorker::request(const Scene& scene,float scale) {
    // Normalize requests to the actual pixel buckets; omit positions and default
    // ASCII glyphs so changing telemetry does not schedule font work.
    Json requests=Json::array();
    for(const auto& layer:scene.layers) for(const auto& c:layer.commands) {
        try {
            auto type=c.value("type",std::string{});if(type!="drawText"&&type!="drawTextWithBackground")continue;
            float size=c.contains("fontSize")&&!c["fontSize"].is_null()?c["fontSize"].get<float>():14.f;
            if(!std::isfinite(size))continue;
            std::string glyphs; // Keep only non-ASCII UTF-8 bytes, preserving full multi-byte characters.
            for(unsigned char ch:c.value("text",std::string{}))if(ch>=128)glyphs+=static_cast<char>(ch);
            requests.push_back({{"type","drawText"},{"fontName",c.value("fontName",std::string("Segoe UI"))},
                {"fontWeight",c.value("fontWeight",400)},{"fontStyle",c.value("fontStyle",std::string{})},
                {"fontSize",std::round(std::clamp(std::max(1.f,size)*scale,4.f,128.f))},{"text",glyphs}});
        }catch(const std::exception&){}
    }
    if(requests.empty())return;
    auto signature=requests.dump();if(signature==signature_)return;signature_=std::move(signature);
    auto s=state_;
    {std::lock_guard lock(s->mutex);s->pending.layers={{"fonts","dynamic",std::move(requests)}};
        if(s->queued)return;s->queued=true;}
    executor().post([weak=std::weak_ptr<State>(s)] {State::process(weak);});
}
void FontWorker::State::process(std::weak_ptr<State> weak) {
        auto s=weak.lock();if(!s)return;Scene request;
            {std::lock_guard lock(s->mutex);if(s->pending.layers.empty()){s->queued=false;return;}request=std::move(s->pending);s->pending={};}
            try {
                if(!s->desired)s->desired=std::make_unique<DashboardFonts>();
                if(s->desired->collect(request)) {
                auto built=std::make_shared<PreparedFonts>();built->fonts=s->desired->snapshot();
                built->fonts->rebuild(built->atlas);
                unsigned char* data=nullptr;int w=0,h=0;built->atlas.GetTexDataAsRGBA32(&data,&w,&h);
                auto image=std::make_shared<ImageAsset>();image->width=w;image->height=h;
                image->rgba.assign(data,data+static_cast<size_t>(w)*h*4);built->image=std::move(image);
                s->desired->markClean();
                {std::lock_guard lock(s->mutex);s->ready.swap(built);}
                } // displaced ready atlas is destroyed outside the state lock
            }catch(const std::exception&){} // Keep the previous font set; never fail a game frame.
        {std::lock_guard lock(s->mutex);if(s->pending.layers.empty()){s->queued=false;return;}}
        executor().post([weak]{State::process(weak);}); // yield to other sessions and retirements
}
void FontWorker::retire(std::shared_ptr<PreparedFonts> fonts){
    if(fonts)executor().post([fonts=std::move(fonts)]{});
}
std::shared_ptr<PreparedFonts> FontWorker::takeReady(){
    std::unique_lock lock(state_->mutex,std::try_to_lock);if(!lock)return {};
    return std::exchange(state_->ready,{});
}
}
