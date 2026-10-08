#include "font_atlas.hpp"
#include <stdexcept>
#include <sstream>
namespace ztmz {
namespace {
std::string family(const Json& c){return c.value("fontName",std::string("Segoe UI"));}
int weight(const Json& c){return c.value("fontWeight",400);}
bool italic(const Json& c){return c.value("fontStyle",std::string{})=="italic";}
}
std::string DashboardFonts::key(const Json& c) {return family(c)+":"+std::to_string(weight(c))+":"+(italic(c)?"i":"n");}
DashboardFonts::DashboardFonts() {
    entries_["Segoe UI:400:n"].selection=selectFont("Segoe UI",400,false);
}
bool DashboardFonts::collect(const Scene& scene) {
    for(const auto& layer:scene.layers) for(const auto& c:layer.commands) {
        try {
            auto type=c.value("type",std::string{});
            if(type!="drawText" && type!="drawTextWithBackground") continue;
            auto name=key(c);auto it=entries_.find(name);
            if(it==entries_.end()) {
                if(entries_.size()>=16) continue;
                Entry entry;entry.selection=selectFont(family(c),weight(c),italic(c));
                it=entries_.emplace(name,std::move(entry)).first;dirty_=true;
            }
            for(auto cp:textCodepoints(c.value("text",std::string{})))
                if(cp>=32 && it->second.glyphs.size()<4096 && it->second.glyphs.insert(cp).second) dirty_=true;
        } catch(const std::exception&) {} // draw() reports malformed commands with layer/type.
    }
    return dirty_;
}
void DashboardFonts::rebuild() {
    auto& io=ImGui::GetIO();io.FontDefault=nullptr;io.Fonts->Clear();
    auto add=[&](Entry& entry) {
        ImFontGlyphRangesBuilder builder;builder.AddRanges(io.Fonts->GetGlyphRangesDefault());
        for(auto cp:entry.glyphs) builder.AddChar(static_cast<ImWchar>(cp));
        entry.ranges.clear();builder.BuildRanges(&entry.ranges);entry.font=nullptr;
        for(const auto& face:entry.selection.faces) {
            ImFontConfig config;config.FontDataOwnedByAtlas=false;config.FontNo=face->index;
            config.MergeMode=entry.font!=nullptr;config.PixelSnapH=false;
            config.OversampleH=2;config.OversampleV=1;
            auto* font=io.Fonts->AddFontFromMemoryTTF(const_cast<unsigned char*>(face->bytes.data()),
                static_cast<int>(face->bytes.size()),48.f,&config,entry.ranges.Data);
            if(!entry.font) entry.font=font;
        }
        if(!entry.font) entry.font=io.Fonts->AddFontDefault();
    };
    auto& defaultFont=entries_.at("Segoe UI:400:n");add(defaultFont);io.FontDefault=defaultFont.font;
    io.FontDefault->Scale=20.f/48.f;
    for(auto& [name,entry]:entries_) if(name!="Segoe UI:400:n") add(entry);
    if(!io.Fonts->Build()) throw std::runtime_error("Cannot build dashboard font atlas");
    dirty_=false;
}
ImFont* DashboardFonts::lookup(const Json& c,std::set<std::string>& warnings) const {
    const auto it=entries_.find(key(c));
    if(it==entries_.end()){warnings.insert("font family limit reached: "+family(c));return ImGui::GetFont();}
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
