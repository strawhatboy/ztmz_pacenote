#include "graphics/dx12/backend.hpp"
#include "graphics/dx12/glass.hpp"
#include <imgui_impl_dx12.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstring>
#include <stdexcept>
#include <deque>
namespace ztmz {
namespace {
using Microsoft::WRL::ComPtr;
void check(HRESULT r){if(FAILED(r)) throw std::runtime_error("DX12 operation failed: "+std::to_string(r));}
D3D12_HEAP_PROPERTIES heap(D3D12_HEAP_TYPE type){D3D12_HEAP_PROPERTIES p{};p.Type=type;p.CreationNodeMask=p.VisibleNodeMask=1;return p;}
D3D12_RESOURCE_DESC bufferDesc(UINT64 bytes){D3D12_RESOURCE_DESC d{};d.Dimension=D3D12_RESOURCE_DIMENSION_BUFFER;d.Width=bytes;d.Height=1;d.DepthOrArraySize=d.MipLevels=1;d.SampleDesc.Count=1;d.Layout=D3D12_TEXTURE_LAYOUT_ROW_MAJOR;return d;}
void transition(ID3D12GraphicsCommandList* list,ID3D12Resource* resource,D3D12_RESOURCE_STATES from,D3D12_RESOURCE_STATES to){
    D3D12_RESOURCE_BARRIER b{};b.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;b.Transition={resource,D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES,from,to};list->ResourceBarrier(1,&b);
}
class Dx12Backend final:public RenderBackend {
    static constexpr unsigned Capacity=4096;
    ComPtr<ID3D12Device> device_;ComPtr<ID3D12CommandQueue> queue_;
    ComPtr<ID3D12DescriptorHeap> srv_,rtv_;ComPtr<ID3D12Fence> fence_;
    HANDLE event_=nullptr;UINT srvStride_=0,rtvStride_=0;
    UINT64 sequence_=0;unsigned current_=0;DXGI_FORMAT format_;bool initialized_=false;
    std::vector<bool> descriptors_=std::vector<bool>(Capacity,false);
    struct Frame {ComPtr<ID3D12CommandAllocator> allocator;ComPtr<ID3D12GraphicsCommandList> list;UINT64 completed=0;};
    std::vector<Frame> frames_;
    std::unique_ptr<Dx12Glass> glass_;
    struct Image {std::shared_ptr<const ImageAsset> source;ComPtr<ID3D12Resource> resource;unsigned slot=0;};
    std::map<std::string,Image> images_;
    struct RetiredImage {UINT64 fence;Image image;};
    struct UploadBatch {UINT64 fence=0;ComPtr<ID3D12CommandAllocator> allocator;ComPtr<ID3D12GraphicsCommandList> list;std::vector<ComPtr<ID3D12Resource>> staging;};
    std::deque<RetiredImage> retired_;
    std::deque<UploadBatch> uploads_;
    void collectCompleted() {
        auto completed=fence_->GetCompletedValue();
        while(!retired_.empty()&&retired_.front().fence<=completed){descriptors_[retired_.front().image.slot]=false;retired_.pop_front();}
        while(!uploads_.empty()&&uploads_.front().fence<=completed)uploads_.pop_front();
    }
    D3D12_CPU_DESCRIPTOR_HANDLE cpu(unsigned slot) const {auto h=srv_->GetCPUDescriptorHandleForHeapStart();h.ptr+=static_cast<SIZE_T>(slot)*srvStride_;return h;}
    D3D12_GPU_DESCRIPTOR_HANDLE gpu(unsigned slot) const {auto h=srv_->GetGPUDescriptorHandleForHeapStart();h.ptr+=static_cast<UINT64>(slot)*srvStride_;return h;}
    // Slot zero belongs to ImGui for the entire backend lifetime. ImGui 1.91.9b
    // frees its font descriptor during invalidation but reuses that same handle
    // during CreateDeviceObjects, without calling the allocator again.
    unsigned allocate(){for(unsigned i=1;i<Capacity;++i) if(!descriptors_[i]){descriptors_[i]=true;return i;}throw std::runtime_error("DX12 SRV descriptor heap exhausted");}
    void wait(UINT64 value) {
        if(!value || fence_->GetCompletedValue()>=value) return;
        check(fence_->SetEventOnCompletion(value,event_));
        for(;;){auto status=WaitForSingleObject(event_,1000);if(status==WAIT_OBJECT_0) return;if(status!=WAIT_TIMEOUT) throw std::runtime_error("DX12 fence wait failed");check(device_->GetDeviceRemovedReason());}
    }
    UINT64 signal(){check(queue_->Signal(fence_.Get(),++sequence_));return sequence_;}
    bool changed(const ImageAssets& images) const {
        if(images.size()!=images_.size()) return true;
        for(const auto& [key,image]:images){auto found=images_.find(key);if(found==images_.end() || found->second.source!=image) return true;}return false;
    }
    Image upload(std::shared_ptr<const ImageAsset> source,ID3D12GraphicsCommandList* list,
        std::vector<ComPtr<ID3D12Resource>>& stagingResources) {
        Image image;image.source=std::move(source);
        D3D12_RESOURCE_DESC d{};d.Dimension=D3D12_RESOURCE_DIMENSION_TEXTURE2D;d.Width=image.source->width;d.Height=image.source->height;d.DepthOrArraySize=d.MipLevels=1;d.Format=DXGI_FORMAT_R8G8B8A8_UNORM;d.SampleDesc.Count=1;
        auto defaultHeap=heap(D3D12_HEAP_TYPE_DEFAULT);
        check(device_->CreateCommittedResource(&defaultHeap,D3D12_HEAP_FLAG_NONE,&d,D3D12_RESOURCE_STATE_COPY_DEST,nullptr,IID_PPV_ARGS(&image.resource)));
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint{};UINT64 total=0;
        device_->GetCopyableFootprints(&d,0,1,0,&footprint,nullptr,nullptr,&total);
        auto uploadHeap=heap(D3D12_HEAP_TYPE_UPLOAD);auto uploadDesc=bufferDesc(total);
        ComPtr<ID3D12Resource> staging;check(device_->CreateCommittedResource(&uploadHeap,D3D12_HEAP_FLAG_NONE,&uploadDesc,D3D12_RESOURCE_STATE_GENERIC_READ,nullptr,IID_PPV_ARGS(&staging)));
        void* mapped=nullptr;D3D12_RANGE read{0,0};check(staging->Map(0,&read,&mapped));
        for(unsigned y=0;y<image.source->height;++y) std::memcpy(static_cast<unsigned char*>(mapped)+footprint.Offset+static_cast<std::size_t>(y)*footprint.Footprint.RowPitch,image.source->rgba.data()+static_cast<std::size_t>(y)*image.source->width*4,image.source->width*4);
        staging->Unmap(0,nullptr);
        D3D12_TEXTURE_COPY_LOCATION from{};from.pResource=staging.Get();from.Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;from.PlacedFootprint=footprint;
        D3D12_TEXTURE_COPY_LOCATION to{};to.pResource=image.resource.Get();to.Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        list->CopyTextureRegion(&to,0,0,0,&from,nullptr);transition(list,image.resource.Get(),D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        stagingResources.push_back(std::move(staging));
        image.slot=allocate();D3D12_SHADER_RESOURCE_VIEW_DESC view{};view.Format=d.Format;view.ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D;view.Shader4ComponentMapping=D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;view.Texture2D.MipLevels=1;
        device_->CreateShaderResourceView(image.resource.Get(),&view,cpu(image.slot));return image;
    }
public:
    Dx12Backend(ID3D12Device* d,ID3D12CommandQueue* q,DXGI_FORMAT f,unsigned count):device_(d),queue_(q),format_(f),frames_(count) {
        if(!d || !q || count<2 || count>16 || q->GetDesc().Type!=D3D12_COMMAND_LIST_TYPE_DIRECT) throw std::invalid_argument("DX12 requires a direct queue and 2..16 frame buffers");
    }
    RenderApi api() const noexcept override{return RenderApi::Dx12;}
    void initialize() override {
        D3D12_DESCRIPTOR_HEAP_DESC desc{};desc.Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;desc.NumDescriptors=Capacity;desc.Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        check(device_->CreateDescriptorHeap(&desc,IID_PPV_ARGS(&srv_)));
        desc.Type=D3D12_DESCRIPTOR_HEAP_TYPE_RTV;desc.NumDescriptors=static_cast<UINT>(frames_.size());desc.Flags=D3D12_DESCRIPTOR_HEAP_FLAG_NONE;
        check(device_->CreateDescriptorHeap(&desc,IID_PPV_ARGS(&rtv_)));
        srvStride_=device_->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);rtvStride_=device_->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
        std::vector<D3D12_CPU_DESCRIPTOR_HANDLE> glassCpu;std::vector<D3D12_GPU_DESCRIPTOR_HANDLE> glassGpu;
        for(size_t i=0;i<frames_.size();++i){auto slot=allocate();glassCpu.push_back(cpu(slot));glassGpu.push_back(gpu(slot));}
        glass_=std::make_unique<Dx12Glass>(device_.Get(),format_,glassCpu,glassGpu);
        check(device_->CreateFence(0,D3D12_FENCE_FLAG_NONE,IID_PPV_ARGS(&fence_)));event_=CreateEventW(nullptr,FALSE,FALSE,nullptr);if(!event_) throw std::runtime_error("Cannot create DX12 fence event");
        for(auto& frame:frames_){check(device_->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,IID_PPV_ARGS(&frame.allocator)));check(device_->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,frame.allocator.Get(),nullptr,IID_PPV_ARGS(&frame.list)));check(frame.list->Close());}
        ImGui_ImplDX12_InitInfo info;info.Device=device_.Get();info.CommandQueue=queue_.Get();info.NumFramesInFlight=static_cast<int>(frames_.size());info.RTVFormat=format_;info.SrvDescriptorHeap=srv_.Get();info.UserData=this;
        info.SrvDescriptorAllocFn=[](ImGui_ImplDX12_InitInfo* i,D3D12_CPU_DESCRIPTOR_HANDLE* c,D3D12_GPU_DESCRIPTOR_HANDLE* g){auto& self=*static_cast<Dx12Backend*>(i->UserData);*c=self.cpu(0);*g=self.gpu(0);};
        info.SrvDescriptorFreeFn=[](ImGui_ImplDX12_InitInfo*,D3D12_CPU_DESCRIPTOR_HANDLE,D3D12_GPU_DESCRIPTOR_HANDLE){}; // dedicated slot, released with the heap
        if(!ImGui_ImplDX12_Init(&info)) throw std::runtime_error("ImGui DX12 initialization failed");initialized_=true;
    }
    bool readyForFrame() const override {return !frames_[current_].completed||fence_->GetCompletedValue()>=frames_[current_].completed;}
    void newFrame() override {glass_->beginFrame();ImGui_ImplDX12_NewFrame();}
    GlassDraw prepareGlass(NativeFrame& native) override {
        auto* frame=dynamic_cast<Dx12Frame*>(&native);
        return frame?glass_->prepare(frame->backBuffer,current_):GlassDraw{};
    }
    void syncImages(const ImageAssets& images) override {
        collectCompleted();
        if(!changed(images)) return;
        for(auto i=images_.begin();i!=images_.end();) {
            auto next=images.find(i->first);
            if(next==images.end() || next->second!=i->second.source){retired_.push_back({sequence_,std::move(i->second)});i=images_.erase(i);}else ++i;
        }
        bool missing=std::any_of(images.begin(),images.end(),[&](const auto& item){return !images_.contains(item.first);});
        if(!missing) return;
        UploadBatch batch;
        check(device_->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,IID_PPV_ARGS(&batch.allocator)));
        check(device_->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,batch.allocator.Get(),nullptr,IID_PPV_ARGS(&batch.list)));
        for(const auto& [key,image]:images) if(!images_.contains(key)) images_.emplace(key,upload(image,batch.list.Get(),batch.staging));
        check(batch.list->Close());uploads_.push_back(std::move(batch));
        ID3D12CommandList* lists[]{uploads_.back().list.Get()};queue_->ExecuteCommandLists(1,lists);
        uploads_.back().fence=signal(); // same queue orders uploads before drawing; no CPU wait
    }
    Texture texture(const std::string& key) const override {auto i=images_.find(key);if(i==images_.end()) return {};return {gpu(i->second.slot).ptr,i->second.source->width,i->second.source->height};}
    void submit(ImDrawData& data,NativeFrame& native) override {
        auto* f=dynamic_cast<Dx12Frame*>(&native);if(!f || !f->backBuffer) throw std::invalid_argument("DX12 back buffer missing");
        // Borrow the application's buffer for this submission. The swapchain owns
        // its lifetime and the application synchronizes teardown of this queue.
        // Retaining it across Present pins the old flip chain to its HWND when a
        // Vulkan interop driver replaces the chain without calling ResizeBuffers.
        auto& frame=frames_[current_];
        check(frame.allocator->Reset());check(frame.list->Reset(frame.allocator.Get(),nullptr));
        auto target=rtv_->GetCPUDescriptorHandleForHeapStart();target.ptr+=static_cast<SIZE_T>(current_)*rtvStride_;
        D3D12_RENDER_TARGET_VIEW_DESC view{};view.Format=format_;view.ViewDimension=D3D12_RTV_DIMENSION_TEXTURE2D;device_->CreateRenderTargetView(f->backBuffer,&view,target);
        f->glassCapture=glass_->capture(frame.list.Get(),f->backBuffer);
        if(!f->glassCapture.copies)transition(frame.list.Get(),f->backBuffer,D3D12_RESOURCE_STATE_PRESENT,D3D12_RESOURCE_STATE_RENDER_TARGET);
        frame.list->OMSetRenderTargets(1,&target,FALSE,nullptr);ID3D12DescriptorHeap* heaps[]{srv_.Get()};frame.list->SetDescriptorHeaps(1,heaps);
        ImGui_ImplDX12_RenderDrawData(&data,frame.list.Get());glass_->endFrame();transition(frame.list.Get(),f->backBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_PRESENT);
        check(frame.list->Close());ID3D12CommandList* lists[]{frame.list.Get()};queue_->ExecuteCommandLists(1,lists);frame.completed=signal();current_=(current_+1)%frames_.size();
    }
    void invalidateDeviceObjects() override {wait(sequence_);glass_->clear();if(initialized_) ImGui_ImplDX12_InvalidateDeviceObjects();} // font rebuild leaves image textures and descriptors intact
    void shutdown() noexcept override {
        try{if(fence_) wait(sequence_);}catch(...){} // device removal permits releasing its resources
        glass_.reset();images_.clear();retired_.clear();uploads_.clear();if(initialized_ || ImGui::GetIO().BackendRendererUserData) ImGui_ImplDX12_Shutdown();initialized_=false;
        frames_.clear();srv_.Reset();rtv_.Reset();fence_.Reset();if(event_) CloseHandle(event_);event_=nullptr;
    }
};
}
std::unique_ptr<RenderBackend> createDx12Backend(ID3D12Device* d,ID3D12CommandQueue* q,DXGI_FORMAT f,unsigned count){return std::make_unique<Dx12Backend>(d,q,f,count);}
}
