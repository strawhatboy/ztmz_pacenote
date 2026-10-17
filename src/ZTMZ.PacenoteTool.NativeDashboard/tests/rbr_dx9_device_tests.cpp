#include "hooks/rbr_dx9_device.hpp"
#include <map>
#include <cstring>
#include <iostream>
#include <stdexcept>

namespace {
void require(bool ok) {if(!ok) throw std::runtime_error("RBR DX9 discovery assertion failed");}
struct Memory {
    std::map<uintptr_t,uint32_t> words;
    Memory() {
        words[0x7ea990]=0x100000;words[0x100028]=0x200000;words[0x2000f4]=0x300000;words[0x300000]=0x400000;
        for(unsigned i=0;i<18;++i)words[0x400000+i*4]=0x500000+i*16;
    }
    bool read(uintptr_t address,void* output,size_t size) {
        for(size_t i=0;i<size;i+=4) {
            auto found=words.find(address+i);if(found==words.end())return false;
            std::memcpy(static_cast<char*>(output)+i,&found->second,4);
        }
        return true;
    }
    auto capture() {return ztmz::readRbrDx9Targets([&](auto a,auto p,auto n){return read(a,p,n);});}
};
}
int main() {
    try {
        Memory valid;auto targets=valid.capture();require(targets.has_value());
        require(targets->reset==0x500100 && targets->present==0x500110);
        for(auto address:{0x7ea990u,0x100028u,0x2000f4u,0x300000u,0x400040u,0x400044u}) {
            Memory missing;missing.words.erase(address);require(!missing.capture());
            Memory zero;zero.words[address]=0;require(!zero.capture());
        }
        for(auto address:{0x7ea990u,0x100028u,0x300000u}) {
            Memory overflow;overflow.words[address]=0xfffffff0;require(!overflow.capture());
        }
        Memory changing;unsigned reads=0;
        auto torn=ztmz::readRbrDx9Targets([&](auto a,auto p,auto n){
            if(a==0x2000f4 && ++reads==2)changing.words[a]=0x600000;
            return changing.read(a,p,n);
        });
        require(!torn);
        std::cout<<"RBR existing-device discovery: valid, missing, null, overflow, incomplete and changing memory passed.\n";
        return 0;
    } catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
