#include "font_atlas.hpp"
#include <iostream>
#include <stdexcept>
using namespace ztmz;
namespace {
void require(bool value,const char* message){if(!value)throw std::runtime_error(message);}
void checkSources(const ImFontAtlas& atlas,const FontSelection& selection) {
    std::size_t referenced=0;
    for(const auto& source:atlas.Sources) {
        bool shared=false;
        for(const auto& face:selection.faces)
            if(source.FontData==face->bytes.data())shared=true;
        require(shared,"Font atlas duplicated a cached font file instead of sharing it");
        require(!source.FontDataOwnedByAtlas,"Atlas would free a shared font file");
        referenced+=source.FontDataSize;
    }
    std::cout<<atlas.Sources.Size<<" sources referencing "<<referenced<<" shared bytes\n";
}
}
int main() {
    try {
        const auto selection=selectFont("Segoe UI",400,false);
        require(!selection.faces.empty(),"Windows fonts unavailable");
        Scene scene;scene.layers.push_back({"fonts","dynamic",Json::array()});
        for(int size:{18,27,54})scene.layers[0].commands.push_back({{"type","drawText"},
            {"fontName","Segoe UI"},{"fontSize",size},{"text","RPM 123 km/h"}});
        DashboardFonts desired;
        require(desired.collect(scene),"Initial font request missing");
        {
            auto fonts=desired.snapshot();ImFontAtlas atlas;fonts->rebuild(atlas);
            checkSources(atlas,selection);
            // Some system fonts omit Latin-1 glyphs, so one fallback may still
            // be needed even for the default range. Do not add the whole chain.
            require(atlas.Sources.Size<4*selection.faces.size(),"Redundant fallback faces were rasterized");
        }
        // Keep the old atlas alive while building a replacement, as the render
        // thread does. Clearing either atlas must not invalidate shared bytes.
        scene.layers[0].commands[0]["text"]="RPM \xe5\xb7\xa6\xe5\x85\xad";
        desired.collect(scene);
        auto first=desired.snapshot();ImFontAtlas oldAtlas;first->rebuild(oldAtlas);
        for(float scale:{0.5f,1.f,0.75f,1.f}) {
            desired.collect(scene,scale);
            auto next=desired.snapshot();ImFontAtlas atlas;next->rebuild(atlas);
            checkSources(atlas,selection);
            std::set<std::string> warnings;
            auto* font=next->lookup(scene.layers[0].commands[0],warnings);
            require(font->FindGlyphNoFallback(0x5de6)&&font->FindGlyphNoFallback(0x516d),"Chinese fallback lost");
            require(warnings.empty(),"Unexpected font warning");
            unsigned char* pixels=nullptr;int w=0,h=0;atlas.GetTexDataAsRGBA32(&pixels,&w,&h);
            require(pixels&&w>0&&h>0,"Missing atlas pixels");
            atlas.Clear();
            require(selection.faces.front()->contains('R'),"Clearing atlas invalidated catalog font");
            require(oldAtlas.Fonts[0]->FindGlyphNoFallback('R'),"Replacement invalidated previous atlas");
        }
        std::cout<<"Font sharing, unused fallbacks, CJK, resizing and overlapping atlas lifetimes passed\n";
        return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
