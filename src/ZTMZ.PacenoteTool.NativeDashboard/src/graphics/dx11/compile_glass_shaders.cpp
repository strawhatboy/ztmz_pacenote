// Build-time compiler only; no shader compilation in the injected process.
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <fstream>
#include <iostream>
#include <iterator>
#include <stdexcept>
#include <string>
int main(int argc,char** argv) {
    try {
        if(argc!=3&&argc!=4) throw std::runtime_error("Expected HLSL input, header output and optional profile suffix");
        const std::string suffix=argc==4?argv[3]:"4_0";
        if(suffix!="3_0"&&suffix!="4_0"&&suffix!="5_0") throw std::runtime_error("Unsupported glass shader profile");
        std::ifstream input(argv[1]);
        if(!input) throw std::runtime_error("Cannot open glass shader");
        std::string source((std::istreambuf_iterator<char>(input)),{});
        std::ofstream output(argv[2],std::ios::binary);
        output<<"#pragma once\n";
        for(auto stage:{"vs","ps"}) {
            Microsoft::WRL::ComPtr<ID3DBlob> code,error;
            const auto entry=std::string(stage)+"Main",profile=std::string(stage)+"_"+suffix;
            if(FAILED(D3DCompile(source.data(),source.size(),argv[1],nullptr,nullptr,entry.c_str(),profile.c_str(),
                D3DCOMPILE_ENABLE_STRICTNESS|D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&code,&error)))
                throw std::runtime_error(error?static_cast<const char*>(error->GetBufferPointer()):"Glass shader compile failed");
            output<<"static constexpr unsigned char glass_"<<stage<<"[]={";
            auto* bytes=static_cast<const unsigned char*>(code->GetBufferPointer());
            for(size_t i=0;i<code->GetBufferSize();++i) output<<unsigned(bytes[i])<<',';
            output<<"};\n";
        }
        if(!output) throw std::runtime_error("Cannot write glass bytecode");
        return 0;
    } catch(const std::exception& e) {std::cerr<<e.what()<<'\n';return 1;}
}
