#include "font_catalog.hpp"
#define STB_TRUETYPE_IMPLEMENTATION
#define STBTT_STATIC
#include <imstb_truetype.h>
#include <filesystem>
#include <fstream>
#include <map>
#include <mutex>
#include <algorithm>
#include <cmath>
#ifdef _WIN32
#include <windows.h>
#include <dwrite.h>
#include <wrl/client.h>
#endif
namespace ztmz {
namespace {
std::mutex fontMutex;
std::map<std::string,FontSelection> selections;
std::map<std::string,std::shared_ptr<const FontFace>> loaded;
std::shared_ptr<const FontFace> load(const std::filesystem::path& path,int index) {
    const auto utf8=path.u8string();
    const auto key=std::string(reinterpret_cast<const char*>(utf8.data()),utf8.size())+":"+std::to_string(index);
    if(auto it=loaded.find(key);it!=loaded.end()) return it->second;
    if(loaded.size()>=64) return {};
    std::ifstream input(path,std::ios::binary|std::ios::ate);
    if(!input) return {};
    auto size=input.tellg();if(size<=0 || size>32*1024*1024) return {};
    auto face=std::make_shared<FontFace>();face->bytes.resize(static_cast<std::size_t>(size));
    input.seekg(0);if(!input.read(reinterpret_cast<char*>(face->bytes.data()),size)) return {};
    face->index=index;face->identity=key;
    stbtt_fontinfo info{};auto offset=stbtt_GetFontOffsetForIndex(face->bytes.data(),index);
    if(offset<0 || !stbtt_InitFont(&info,face->bytes.data(),offset)) return {};
    loaded.emplace(key,face);return face;
}
#ifdef _WIN32
using Microsoft::WRL::ComPtr;
std::wstring wide(const std::string& text) {
    auto size=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,text.data(),static_cast<int>(text.size()),nullptr,0);
    if(size<=0) return {};
    std::wstring result(size,L'\0');MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,text.data(),static_cast<int>(text.size()),result.data(),size);return result;
}
std::shared_ptr<const FontFace> systemFont(const std::string& name,int weight,bool italic) {
    ComPtr<IDWriteFactory> factory;
    if(FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED,__uuidof(IDWriteFactory),reinterpret_cast<IUnknown**>(factory.GetAddressOf())))) return {};
    ComPtr<IDWriteFontCollection> collection;if(FAILED(factory->GetSystemFontCollection(&collection))) return {};
    UINT32 index=0;BOOL exists=FALSE;auto familyName=wide(name);
    if(FAILED(collection->FindFamilyName(familyName.c_str(),&index,&exists))||!exists) return {};
    ComPtr<IDWriteFontFamily> family;ComPtr<IDWriteFont> font;ComPtr<IDWriteFontFace> face;
    if(FAILED(collection->GetFontFamily(index,&family)) || FAILED(family->GetFirstMatchingFont(
        static_cast<DWRITE_FONT_WEIGHT>(std::clamp(weight,100,900)),DWRITE_FONT_STRETCH_NORMAL,
        italic?DWRITE_FONT_STYLE_ITALIC:DWRITE_FONT_STYLE_NORMAL,&font)) || FAILED(font->CreateFontFace(&face))) return {};
    UINT32 count=0;if(FAILED(face->GetFiles(&count,nullptr))||count!=1) return {};
    ComPtr<IDWriteFontFile> file;if(FAILED(face->GetFiles(&count,file.GetAddressOf()))) return {};
    const void* key=nullptr;UINT32 keySize=0;ComPtr<IDWriteFontFileLoader> loader;ComPtr<IDWriteLocalFontFileLoader> local;
    if(FAILED(file->GetReferenceKey(&key,&keySize))||FAILED(file->GetLoader(&loader))||FAILED(loader.As(&local))) return {};
    UINT32 length=0;if(FAILED(local->GetFilePathLengthFromKey(key,keySize,&length))) return {};
    std::wstring path(length+1,L'\0');if(FAILED(local->GetFilePathFromKey(key,keySize,path.data(),length+1))) return {};
    path.resize(length);return load(path,static_cast<int>(face->GetIndex()));
}
std::filesystem::path moduleDirectory() {
    HMODULE module=nullptr;
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&moduleDirectory),&module)) return {};
    std::wstring path(32768,L'\0');auto length=GetModuleFileNameW(module,path.data(),static_cast<DWORD>(path.size()));
    if(!length||length>=path.size()) return {};path.resize(length);return std::filesystem::path(path).parent_path();
}
std::filesystem::path bundledFontDirectory() {
    const auto directory=moduleDirectory();
    // The injected module is in <ZTMZ>/native-dashboard/win-x64. Resolve from
    // that DLL, never the game executable or the game's working directory.
    if(directory.filename()=="win-x64" && directory.parent_path().filename()=="native-dashboard")
        return directory.parent_path().parent_path()/"fonts";
    // Standalone native viewer and CMake test outputs keep fonts beside the module.
    return directory/"fonts";
}
#endif
}
bool FontFace::contains(unsigned int cp) const {
    stbtt_fontinfo info{};stbtt_InitFont(&info,bytes.data(),stbtt_GetFontOffsetForIndex(bytes.data(),index));
    return stbtt_FindGlyphIndex(&info,static_cast<int>(cp))!=0;
}
float FontFace::advance(unsigned int cp,float size) const {
    stbtt_fontinfo info{};stbtt_InitFont(&info,bytes.data(),stbtt_GetFontOffsetForIndex(bytes.data(),index));
    int advance=0,bearing=0;stbtt_GetCodepointHMetrics(&info,static_cast<int>(cp),&advance,&bearing);
    return advance*stbtt_ScaleForPixelHeight(&info,size);
}
FontSelection selectFont(const std::string& family,int weight,bool italic) {
    std::lock_guard lock(fontMutex);
    const auto key=family+":"+std::to_string(weight)+":"+(italic?"i":"n");
    if(auto it=selections.find(key);it!=selections.end()) return it->second;
    FontSelection result;
#ifdef _WIN32
    std::shared_ptr<const FontFace> primary;
    if(family=="WRC Clean") {
        primary=load(bundledFontDirectory()/(weight>=600?"WRCCleanBold.ttf":"WRCCleanRoman.ttf"),0);
        result.substituted=italic;
    } else primary=systemFont(family,weight,italic);
    if(primary) result.faces.push_back(primary);
    else result.substituted=true;
    for(const auto* fallback:{"Segoe UI","Microsoft YaHei","Yu Gothic","Malgun Gothic"}) {
        auto font=systemFont(fallback,weight,italic);
        if(font && std::none_of(result.faces.begin(),result.faces.end(),[&](const auto& f){return f->identity==font->identity;})) result.faces.push_back(font);
    }
#else
    result.substituted=true;
#endif
    // Bound process-lifetime metadata caching for arbitrary dashboard font names.
    if(selections.size()<64) selections.emplace(key,result);
    return result;
}
std::vector<unsigned int> textCodepoints(const std::string& text) {
    std::vector<unsigned int> result;
    for(std::size_t i=0;i<text.size();) {
        auto c=static_cast<unsigned char>(text[i++]);unsigned int cp=c;int extra=0;
        if(c>=0xf0){cp=c&7;extra=3;}else if(c>=0xe0){cp=c&15;extra=2;}else if(c>=0xc0){cp=c&31;extra=1;}
        for(int n=0;n<extra;++n) {if(i==text.size() || (static_cast<unsigned char>(text[i])&0xc0)!=0x80){cp=0xfffd;break;}cp=(cp<<6)|(static_cast<unsigned char>(text[i++])&63);}
        result.push_back(cp<=0x10ffff?cp:0xfffd);
    }
    return result;
}
TextSize measureText(const FontSelection& fonts,float size,const std::string& text) {
    TextSize result{0,size};float line=0;
    for(auto cp:textCodepoints(text)) {
        if(cp=='\r') continue;
        if(cp=='\n'){result.width=std::max(result.width,line);line=0;result.height+=size;continue;}
        auto face=std::find_if(fonts.faces.begin(),fonts.faces.end(),[&](const auto& f){return f->contains(cp);});
        if(face!=fonts.faces.end()) line+=(*face)->advance(cp,size);
        else if(!fonts.faces.empty()) line+=fonts.faces.front()->advance('?',size);
        else line+=size*0.6f;
    }
    result.width=std::max(result.width,line);return result;
}
}
