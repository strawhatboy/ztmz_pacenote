#include "font_atlas.hpp"
#include <stdexcept>
#include <sstream>
#include <algorithm>
#include <cmath>
namespace ztmz {
namespace {
std::string family(const Json& c){return c.value("fontName",std::string("Segoe UI"));}
int weight(const Json& c){return c.value("fontWeight",400);}
bool italic(const Json& c){return c.value("fontStyle",std::string{})=="italic";}
}
int DashboardFonts::pixels(const Json& c) const {
    float size=c.contains("fontSize")&&!c["fontSize"].is_null()?c["fontSize"].get<float>():14.f;
    if(!std::isfinite(size)) throw std::invalid_argument("Invalid font size");
    return static_cast<int>(std::round(std::clamp(std::max(1.f,size)*scale_,4.f,128.f)));
}
std::string DashboardFonts::key(const Json& c) const {return family(c)+":"+std::to_string(weight(c))+":"+(italic(c)?"i":"n")+":"+std::to_string(pixels(c));}
DashboardFonts::DashboardFonts() {
    entries_["Segoe UI:400:n:20"].selection=selectFont("Segoe UI",400,false);
}
bool DashboardFonts::collect(const Scene& scene,float scale) {
    if(!std::isfinite(scale)||scale<=0) throw std::invalid_argument("Invalid font scale");
    scale_=scale;
    for(const auto& layer:scene.layers) for(const auto& c:layer.commands) {
        try {
            auto type=c.value("type",std::string{});
            if(type!="drawText" && type!="drawTextWithBackground") continue;
            auto name=key(c);auto it=entries_.find(name);
            if(it==entries_.end()) {
                if(entries_.size()>=64) continue;
                Entry entry;entry.pixels=pixels(c);entry.selection=selectFont(family(c),weight(c),italic(c));
                it=entries_.emplace(name,std::move(entry)).first;dirty_=true;
            }
            for(auto cp:textCodepoints(c.value("text",std::string{})))
                // Every atlas already includes ImGui's default U+0020..U+00FF
                // range. Changing speed/time digits must not rebuild it.
                if(cp>255 && it->second.glyphs.size()<4096 && it->second.glyphs.insert(cp).second) dirty_=true;
        } catch(const std::exception&) {} // draw() reports malformed commands with layer/type.
    }
    return dirty_;
}
void DashboardFonts::rebuild() {
    auto& io=ImGui::GetIO();io.FontDefault=nullptr;
    rebuild(*io.Fonts);io.FontDefault=entries_.at("Segoe UI:400:n:20").font;
}
std::unique_ptr<DashboardFonts> DashboardFonts::snapshot() const {
    auto copy=std::make_unique<DashboardFonts>();copy->entries_.clear();copy->scale_=scale_;
    for(const auto& [key,entry]:entries_) {
        Entry next;next.selection=entry.selection;next.glyphs=entry.glyphs;next.pixels=entry.pixels;
        copy->entries_.emplace(key,std::move(next));
    }
    return copy;
}
void DashboardFonts::rebuild(ImFontAtlas& atlas) {
    atlas.Clear();
    auto add=[&](Entry& entry) {
        ImFontGlyphRangesBuilder builder;builder.AddRanges(atlas.GetGlyphRangesDefault());
        for(auto cp:entry.glyphs) builder.AddChar(static_cast<ImWchar>(cp));
        entry.ranges.clear();builder.BuildRanges(&entry.ranges);entry.font=nullptr;
        for(const auto& face:entry.selection.faces) {
            ImFontConfig config;config.FontDataOwnedByAtlas=false;config.FontNo=face->index;
            config.MergeMode=entry.font!=nullptr;config.PixelSnapH=false;
            config.OversampleH=2;config.OversampleV=1;
            auto* font=atlas.AddFontFromMemoryTTF(const_cast<unsigned char*>(face->bytes.data()),
                static_cast<int>(face->bytes.size()),static_cast<float>(entry.pixels),&config,entry.ranges.Data);
            if(!entry.font) entry.font=font;
        }
        if(!entry.font) {ImFontConfig config;config.SizePixels=static_cast<float>(entry.pixels);entry.font=atlas.AddFontDefault(&config);}
    };
    auto& defaultFont=entries_.at("Segoe UI:400:n:20");add(defaultFont);
    for(auto& [name,entry]:entries_) if(name!="Segoe UI:400:n:20") add(entry);
    if(!atlas.Build()) throw std::runtime_error("Cannot build dashboard font atlas");
    dirty_=false;
}
ImFont* DashboardFonts::lookup(const Json& c,std::set<std::string>& warnings) const {
    auto it=entries_.find(key(c));
    if(it==entries_.end()) {
        auto name=key(c);auto prefix=name.substr(0,name.rfind(':')+1);int nearest=100000;
        for(auto candidate=entries_.begin();candidate!=entries_.end();++candidate) {
            int distance=std::abs(candidate->second.pixels-pixels(c));
            if(candidate->first.starts_with(prefix)&&distance<nearest){it=candidate;nearest=distance;}
        }
        if(it==entries_.end())return ImGui::GetFont(); // background preparation is pending
    }
    const auto& entry=it->second;
    if(entry.selection.substituted) warnings.insert("font substituted: "+family(c));
    for(auto cp:textCodepoints(c.value("text",std::string{})))
        if(cp>=32 && !entry.font->FindGlyphNoFallback(static_cast<ImWchar>(cp))) {
            std::ostringstream code;code<<std::hex<<std::uppercase<<cp;
            warnings.insert("missing glyph in system fonts: U+"+code.str());break;
        }
    return entry.font;
}
}
