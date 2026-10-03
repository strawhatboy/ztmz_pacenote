#include "renderer.hpp"
#include <algorithm>
#include <cmath>
namespace ztmz {
namespace {
ImU32 rgba(std::uint32_t argb) { return IM_COL32((argb>>16)&255,(argb>>8)&255,argb&255,(argb>>24)&255); }
ImVec4 mix(ImVec4 a,ImVec4 b,float t) { return {a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t,a.w+(b.w-a.w)*t}; }
ImVec4 sampleGradient(const Json& gradient,ImVec2 p) {
    const auto& colors=gradient.at("colors");
    if(colors.empty()) return {0,0,0,0};
    float t=0;
    if(gradient.value("type",std::string{})=="radial") {
        float rx=std::max(0.001f,std::abs(gradient.value("radiusX",gradient.value("r1",1.f))));
        float ry=std::max(0.001f,std::abs(gradient.value("radiusY",rx)));
        float dx=(p.x-gradient.value("centerX",gradient.value("x0",0.f)))/rx;
        float dy=(p.y-gradient.value("centerY",gradient.value("y0",0.f)))/ry;
        t=std::sqrt(dx*dx+dy*dy);
    } else {
        float x=gradient.value("x0",0.f),y=gradient.value("y0",0.f);
        float dx=gradient.value("x1",1.f)-x,dy=gradient.value("y1",1.f)-y;
        float denominator=dx*dx+dy*dy;
        t=denominator>0?((p.x-x)*dx+(p.y-y)*dy)/denominator:0;
    }
    float stop=std::clamp(t,0.f,1.f)*static_cast<float>(colors.size()-1);
    auto i=static_cast<std::size_t>(stop),j=std::min(i+1,colors.size()-1);
    return mix(ImGui::ColorConvertU32ToFloat4(rgba(colors[i].get<std::uint32_t>())),
        ImGui::ColorConvertU32ToFloat4(rgba(colors[j].get<std::uint32_t>())),stop-static_cast<float>(i));
}
// Subdivide gradient geometry so radial/multi-stop fills have interior color samples.
void gradientTriangle(ImDrawList* out,const ImDrawVert& a,const ImDrawVert& b,const ImDrawVert& c,
                      const Json& gradient,ImVec2 offset,float scale,int depth,std::size_t& budget) {
    auto distance=[](ImVec2 x,ImVec2 y){return std::hypot(x.x-y.x,x.y-y.y);};
    if(depth<4 && budget>12 && std::max({distance(a.pos,b.pos),distance(b.pos,c.pos),distance(c.pos,a.pos)})>24*scale) {
        auto middle=[](const ImDrawVert& x,const ImDrawVert& y){
            ImDrawVert v;v.pos={(x.pos.x+y.pos.x)/2,(x.pos.y+y.pos.y)/2};v.uv={(x.uv.x+y.uv.x)/2,(x.uv.y+y.uv.y)/2};
            v.col=ImGui::ColorConvertFloat4ToU32(mix(ImGui::ColorConvertU32ToFloat4(x.col),ImGui::ColorConvertU32ToFloat4(y.col),0.5f));return v;
        };
        auto ab=middle(a,b),bc=middle(b,c),ca=middle(c,a);
        gradientTriangle(out,a,ab,ca,gradient,offset,scale,depth+1,budget);
        gradientTriangle(out,ab,b,bc,gradient,offset,scale,depth+1,budget);
        gradientTriangle(out,ca,bc,c,gradient,offset,scale,depth+1,budget);
        gradientTriangle(out,ab,bc,ca,gradient,offset,scale,depth+1,budget);return;
    }
    if(budget<3) return;
    budget-=3;out->PrimReserve(3,3);
    for(const auto* vertex:{&a,&b,&c}) {
        auto color=sampleGradient(gradient,{(vertex->pos.x-offset.x)/scale,(vertex->pos.y-offset.y)/scale});
        color.w*=ImGui::ColorConvertU32ToFloat4(vertex->col).w;
        out->PrimVtx(vertex->pos,vertex->uv,ImGui::ColorConvertFloat4ToU32(color));
    }
}
}
std::set<std::string> draw(const Scene& scene,const TextureLookup& textures) {
    auto* output=ImGui::GetBackgroundDrawList();
    const auto size=ImGui::GetIO().DisplaySize;
    float scale=std::min(size.x/std::max(1,scene.width),size.y/std::max(1,scene.height));
    if(scale<=0) return {};
    ImVec2 offset{(size.x-scene.width*scale)/2,(size.y-scene.height*scale)/2};
    auto point=[&](float x,float y){return ImVec2{offset.x+x*scale,offset.y+y*scale};};
    std::set<std::string> unsupported;
    std::size_t gradientBudget=250000;
    for(const auto& layer:scene.layers) {
        int clips=0;
        output->PushClipRect(offset,point(static_cast<float>(scene.width),static_cast<float>(scene.height)),true);
        for(const auto& c:layer.commands) {
            try {
                const auto type=c.value("type",std::string{});
                auto n=[&](const char* key,float fallback=0.f){return c.contains(key)&&!c[key].is_null()?c[key].get<float>():fallback;};
                auto color=rgba(c.value("color",0xffffffffu));
                const bool gradient=c.contains("gradient")&&!c["gradient"].is_null();
                ImDrawList scratch(ImGui::GetDrawListSharedData());
                auto* dl=gradient?&scratch:output;
                if(gradient) {scratch._ResetForNewFrame();scratch.PushClipRectFullScreen();scratch.PushTextureID(ImGui::GetIO().Fonts->TexID);color=IM_COL32_WHITE;}
                auto a=point(n("x"),n("y")),b=point(n("x")+n("width"),n("y")+n("height"));
                auto thickness=n("thickness",1)*scale;
                if(type=="clipRegionStart") {output->PushClipRect(a,b,true);++clips;}
                else if(type=="clipRegionEnd") {if(clips>0){output->PopClipRect();--clips;}}
                else if(type=="drawLine") dl->AddLine(point(n("x1"),n("y1")),point(n("x2"),n("y2")),color,thickness);
                else if(type=="fillRectangle") dl->AddRectFilled(a,b,color);
                else if(type=="drawRectangle") dl->AddRect(a,b,color,0,0,thickness);
                else if(type=="fillRoundedRectangle") dl->AddRectFilled(a,b,color,n("radius")*scale);
                else if(type=="drawRoundedRectangle") dl->AddRect(a,b,color,n("radius")*scale,0,thickness);
                else if(type=="fillCircle") dl->AddCircleFilled(a,std::abs(n("radius"))*scale,color);
                else if(type=="drawCircle") dl->AddCircle(a,std::abs(n("radius"))*scale,color,0,thickness);
                else if(type=="fillEllipse") {
                    ImVec2 points[64];
                    for(int i=0;i<64;++i){float angle=i*6.2831853f/64;points[i]=point(n("x")+std::cos(angle)*n("radiusX"),n("y")+std::sin(angle)*n("radiusY"));}
                    dl->AddConvexPolyFilled(points,64,color);
                } else if(type=="fillGeometry" || type=="drawGeometry") {
                    std::vector<ImVec2> points;
                    for(const auto& p:c.at("points")) points.push_back(point(p.at("x").get<float>(),p.at("y").get<float>()));
                    if(points.size()>1 && points.front().x==points.back().x && points.front().y==points.back().y) points.pop_back();
                    if(type=="fillGeometry" && points.size()>=3) {
                        // ImGui requires clockwise screen-space winding for filled polygons.
                        double area=0;for(std::size_t i=0;i<points.size();++i){auto j=(i+1)%points.size();area+=points[i].x*points[j].y-points[j].x*points[i].y;}
                        if(area<0) std::reverse(points.begin(),points.end());
                        dl->AddConcavePolyFilled(points.data(),static_cast<int>(points.size()),color);
                    } else if(points.size()>=2) dl->AddPolyline(points.data(),static_cast<int>(points.size()),color,c.value("closed",false)?ImDrawFlags_Closed:0,thickness);
                } else if(type=="drawText" || type=="drawTextWithBackground") {
                    const float fontSize=std::max(1.f,n("fontSize",14))*scale;
                    if(type=="drawTextWithBackground") {
                        float padding=fontSize*0.25f;
                        dl->AddRectFilled({a.x-padding,a.y-padding},{a.x+n("textWidth")*scale+padding,a.y+n("textHeight")*scale+padding},rgba(c.value("bgColor",0u)));
                    }
                    dl->AddText(ImGui::GetFont(),fontSize,a,color,c.value("text",std::string{}).c_str());
                    unsupported.insert("font family/style and exact text metrics");
                } else if(type=="drawImage" || type=="drawImageRegion") {
                    auto texture=textures?textures(c.at("imageKey").get<std::string>()):Texture{};
                    if(!texture.id) unsupported.insert("image not loaded: "+c.at("imageKey").get<std::string>());
                    else if(type=="drawImage") {
                        b=point(n("x")+n("width",static_cast<float>(texture.width)),n("y")+n("height",static_cast<float>(texture.height)));
                        dl->AddImage(texture.id,a,b,{0,0},{1,1},IM_COL32(255,255,255,static_cast<int>(std::clamp(n("opacity",1),0.f,1.f)*255)));
                    } else {
                        dl->AddImage(texture.id,point(n("destX"),n("destY")),point(n("destX")+n("destWidth"),n("destY")+n("destHeight")),
                            {n("srcX")/texture.width,n("srcY")/texture.height},{(n("srcX")+n("srcWidth"))/texture.width,(n("srcY")+n("srcHeight"))/texture.height});
                    }
                } else if(type!="clear") unsupported.insert(type);
                if(gradient) {
                    for(int i=0;i+2<scratch.IdxBuffer.Size;i+=3)
                        gradientTriangle(output,scratch.VtxBuffer[scratch.IdxBuffer[i]],scratch.VtxBuffer[scratch.IdxBuffer[i+1]],scratch.VtxBuffer[scratch.IdxBuffer[i+2]],c.at("gradient"),offset,scale,0,gradientBudget);
                    if(gradientBudget<3) unsupported.insert("gradient tessellation budget exceeded");
                }
            } catch(const std::exception&) {unsupported.insert("malformed command");}
        }
        while(clips>0){output->PopClipRect();--clips;}
        output->PopClipRect();
    }
    return unsupported;
}
}
