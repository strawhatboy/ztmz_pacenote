#include <d3dcompiler.h>
#include <wrl/client.h>
#include <fstream>
#include <iostream>
#include <iterator>
#include <string>
#include <stdexcept>
// Build tool, not shipped. Extract the shaders from the pinned ImGui backend,
// so updating ImGui cannot silently leave a separate handwritten shader stale.
int main(int argc,char** argv) {
    try {
        if(argc!=4)throw std::runtime_error("Expected backend source, shader model and output header");
        std::ifstream input(argv[1]);if(!input)throw std::runtime_error("Cannot read backend source");
        std::string source((std::istreambuf_iterator<char>(input)),{}),header="#pragma once\n";
        for(auto name:{"vertexShader","pixelShader"}) {
            auto start=source.find(std::string("static const char* ")+name+" =");
            if(start==std::string::npos)throw std::runtime_error("Pinned ImGui shader declaration changed");
            start=source.find('"',start);auto end=source.find("\";",start+1);
            if(end==std::string::npos)throw std::runtime_error("Cannot extract shader");
            auto text=source.substr(start+1,end-start-1);
            // ImGui uses C++ backslash-newline continuations in these literals.
            for(size_t at=0;(at=text.find("\\\n",at))!=std::string::npos;)text.erase(at,2);
            auto profile=std::string(name[0]=='v'?"vs_":"ps_")+argv[2];
            Microsoft::WRL::ComPtr<ID3DBlob> code,error;
            auto hr=D3DCompile(text.data(),text.size(),nullptr,nullptr,nullptr,"main",profile.c_str(),0,0,&code,&error);
            if(FAILED(hr))throw std::runtime_error(error?static_cast<const char*>(error->GetBufferPointer()):"Shader compile failed");
            header+="static constexpr unsigned char "+std::string(name)+"Bytecode[]={";
            auto bytes=static_cast<const unsigned char*>(code->GetBufferPointer());
            for(size_t i=0;i<code->GetBufferSize();++i)header+=std::to_string(bytes[i])+",";
            header+="};\n";
        }
        std::ofstream output(argv[3],std::ios::binary);output<<header;
        if(!output)throw std::runtime_error("Cannot write bytecode header");
        return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
