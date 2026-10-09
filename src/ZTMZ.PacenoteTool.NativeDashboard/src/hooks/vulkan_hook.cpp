#include "hooks/overlay_hook.hpp"
#include "graphics/vulkan/backend.hpp"
#include "graphics/render_session.hpp"
#include "graphics/async_preparation.hpp"
#include <windows.h>
#include <MinHook.h>
#include <array>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstring>
#include <map>
#include <stdexcept>
namespace ztmz {
namespace {
class VulkanHook final:public OverlayHook {
    static inline VulkanHook* self_=nullptr;
    PFN_vkGetInstanceProcAddr gipa_=nullptr;PFN_vkGetDeviceProcAddr gdpa_=nullptr;
    PFN_vkEnumeratePhysicalDevices enumerate_=nullptr;PFN_vkEnumeratePhysicalDeviceGroups groups_=nullptr;
    PFN_vkCreateDevice createDevice_=nullptr;PFN_vkDestroyDevice destroyDevice_=nullptr;
    PFN_vkDestroyInstance destroyInstance_=nullptr;
    PFN_vkGetDeviceQueue getQueue_=nullptr;PFN_vkGetDeviceQueue2 getQueue2_=nullptr;
    PFN_vkCreateSwapchainKHR createSwap_=nullptr;PFN_vkDestroySwapchainKHR destroySwap_=nullptr;
    PFN_vkQueuePresentKHR present_=nullptr;
    PFN_vkCreateWin32SurfaceKHR createSurface_=nullptr;
    PFN_vkDestroySurfaceKHR destroySurface_=nullptr;
    std::map<VkSurfaceKHR,HWND> windows_;
    VkInstance dispatchInstance_=VK_NULL_HANDLE;
    SharedScene& shared_;RenderApiRouter& router_;std::atomic<bool> enabled_{false};std::recursive_mutex mutex_;
    bool installed_=false;VkSwapchainKHR selected_=VK_NULL_HANDLE;
    std::map<VkPhysicalDevice,VkInstance> physical_;
    struct Device {VkInstance instance;VkPhysicalDevice physical;std::vector<VkQueueFamilyProperties> families;std::vector<uint32_t> graphicsFamilies;};
    struct Queue {VkDevice device;uint32_t family;};
    struct Swap {VkDevice device=VK_NULL_HANDLE;VkFormat format{};VkExtent2D extent{};uint32_t minimum=2;VkImageUsageFlags usage{};bool safeOwnership=false;std::vector<uint32_t> concurrentFamilies;
        std::vector<VkImage> images;std::unique_ptr<RenderSession> renderer;std::chrono::steady_clock::time_point previous;
        AsyncPreparation<RenderSession> preparation;
        std::vector<VkSemaphore> signals;
        Swap()=default;Swap(Swap&&)=default;
        void clearRenderer(){preparation.drain();renderer.reset();}
        ~Swap(){clearRenderer();for(auto signal:signals)if(signal)vkDestroySemaphore(device,signal,nullptr);}
    };
    std::map<VkSwapchainKHR,HWND> swapWindows_;
    void reportFailure(const char* message) {
        auto& progress=shared_.progress;std::lock_guard lock(progress.mutex);
        progress.error=std::string(message).substr(0,512);progress.lastFrameTick=0;
    }
    static VkResult VKAPI_CALL createSurface(VkInstance instance,const VkWin32SurfaceCreateInfoKHR* info,const VkAllocationCallbacks* allocator,VkSurfaceKHR* surface) {
        auto result=self_->createSurface_(instance,info,allocator,surface);
        if(result==VK_SUCCESS) {std::lock_guard lock(self_->mutex_);self_->windows_[*surface]=info->hwnd;}
        return result;
    }
    static void VKAPI_CALL destroySurface(VkInstance instance,VkSurfaceKHR surface,const VkAllocationCallbacks* allocator) {
        {std::lock_guard lock(self_->mutex_);self_->windows_.erase(surface);}
        self_->destroySurface_(instance,surface,allocator);
    }
    std::map<VkDevice,Device> devices_;std::map<VkQueue,Queue> queues_;std::map<VkSwapchainKHR,Swap> swaps_;
    void clear() noexcept {for(auto& [key,s]:swaps_){(void)key;s.clearRenderer();}selected_=VK_NULL_HANDLE;router_.release(RenderApi::Vulkan);}
    void queue(VkDevice d,uint32_t family,VkQueue value) {
        if(!value) return;std::lock_guard lock(mutex_);
        auto device=devices_.find(d);if(device==devices_.end() || family>=device->second.families.size()) return;
        if(device->second.families[family].queueFlags&VK_QUEUE_GRAPHICS_BIT) queues_[value]={d,family};
    }
    static VkResult VKAPI_CALL enumerate(VkInstance i,uint32_t* count,VkPhysicalDevice* devices) {
        auto result=self_->enumerate_(i,count,devices);
        if(devices && (result==VK_SUCCESS || result==VK_INCOMPLETE)) try{std::lock_guard lock(self_->mutex_);for(uint32_t n=0;n<*count;++n) self_->physical_[devices[n]]=i;}catch(const std::exception& e){OutputDebugStringA(e.what());}return result;
    }
    static VkResult VKAPI_CALL groups(VkInstance i,uint32_t* count,VkPhysicalDeviceGroupProperties* devices) {
        auto result=self_->groups_(i,count,devices);
        if(devices && (result==VK_SUCCESS || result==VK_INCOMPLETE)) try{std::lock_guard lock(self_->mutex_);for(uint32_t n=0;n<*count;++n) for(uint32_t d=0;d<devices[n].physicalDeviceCount;++d) self_->physical_[devices[n].physicalDevices[d]]=i;}catch(const std::exception& e){OutputDebugStringA(e.what());}return result;
    }
    static VkResult VKAPI_CALL createDevice(VkPhysicalDevice p,const VkDeviceCreateInfo* info,const VkAllocationCallbacks* allocator,VkDevice* out) {
        auto result=self_->createDevice_(p,info,allocator,out);if(result!=VK_SUCCESS) return result;
        try {
            for(auto* next=static_cast<const VkBaseInStructure*>(info->pNext);next;next=next->pNext)
                if(next->sType==VK_STRUCTURE_TYPE_DEVICE_GROUP_DEVICE_CREATE_INFO && reinterpret_cast<const VkDeviceGroupDeviceCreateInfo*>(next)->physicalDeviceCount>1) return result;
            std::lock_guard lock(self_->mutex_);auto instance=self_->physical_.find(p);if(instance==self_->physical_.end()) return result;
            // Keep a single instance's loader dispatch; never mutate dispatch while
            // another instance owns live overlay resources.
            if(self_->dispatchInstance_ && self_->dispatchInstance_!=instance->second) return result;
            if(!self_->dispatchInstance_) {volkLoadInstance(instance->second);self_->dispatchInstance_=instance->second;}
            uint32_t count=0;vkGetPhysicalDeviceQueueFamilyProperties(p,&count,nullptr);Device device{instance->second,p,std::vector<VkQueueFamilyProperties>(count),{}};vkGetPhysicalDeviceQueueFamilyProperties(p,&count,device.families.data());
            for(uint32_t i=0;i<info->queueCreateInfoCount;++i){const auto& q=info->pQueueCreateInfos[i];if(q.flags==0 && q.queueFamilyIndex<count && (device.families[q.queueFamilyIndex].queueFlags&VK_QUEUE_GRAPHICS_BIT)) device.graphicsFamilies.push_back(q.queueFamilyIndex);}
            self_->devices_.emplace(*out,std::move(device));
        } catch(const std::exception& e){OutputDebugStringA(e.what());}
        return result;
    }
    static void VKAPI_CALL getQueue(VkDevice d,uint32_t family,uint32_t index,VkQueue* out){self_->getQueue_(d,family,index,out);try{self_->queue(d,family,*out);}catch(const std::exception& e){OutputDebugStringA(e.what());}}
    static void VKAPI_CALL getQueue2(VkDevice d,const VkDeviceQueueInfo2* info,VkQueue* out){self_->getQueue2_(d,info,out);if(info->flags==0) try{self_->queue(d,info->queueFamilyIndex,*out);}catch(const std::exception& e){OutputDebugStringA(e.what());}}
    static VkResult VKAPI_CALL createSwap(VkDevice d,const VkSwapchainCreateInfoKHR* info,const VkAllocationCallbacks* a,VkSwapchainKHR* out) {
        auto result=self_->createSwap_(d,info,a,out);if(result!=VK_SUCCESS) return result;
        try {
            std::lock_guard lock(self_->mutex_);auto device=self_->devices_.find(d);if(device==self_->devices_.end()) return result;
            if(info->imageArrayLayers!=1 || (info->flags&VK_SWAPCHAIN_CREATE_PROTECTED_BIT_KHR)) return result;
            uint32_t count=0;if(vkGetSwapchainImagesKHR(d,*out,&count,nullptr)!=VK_SUCCESS) return result;
            Swap swap;swap.device=d;swap.format=info->imageFormat;swap.extent=info->imageExtent;swap.minimum=info->minImageCount;swap.usage=info->imageUsage;
            swap.safeOwnership=info->imageSharingMode==VK_SHARING_MODE_CONCURRENT || device->second.graphicsFamilies.size()==1;
            swap.images.resize(count);swap.signals.resize(count);swap.previous=std::chrono::steady_clock::now();
            if(info->imageSharingMode==VK_SHARING_MODE_CONCURRENT) swap.concurrentFamilies.assign(info->pQueueFamilyIndices,info->pQueueFamilyIndices+info->queueFamilyIndexCount);
            if(vkGetSwapchainImagesKHR(d,*out,&count,swap.images.data())==VK_SUCCESS) {
                self_->swaps_.emplace(*out,std::move(swap));
                if(auto window=self_->windows_.find(info->surface);window!=self_->windows_.end()) self_->swapWindows_[*out]=window->second;
            }
        } catch(const std::exception& e){OutputDebugStringA(e.what());}
        return result;
    }
    static void VKAPI_CALL destroySwap(VkDevice d,VkSwapchainKHR swap,const VkAllocationCallbacks* a) {
        {std::lock_guard lock(self_->mutex_);self_->swaps_.erase(swap);self_->swapWindows_.erase(swap);if(self_->selected_==swap){self_->selected_=VK_NULL_HANDLE;self_->router_.release(RenderApi::Vulkan);}}
        self_->destroySwap_(d,swap,a);
    }
    static void VKAPI_CALL destroyDevice(VkDevice d,const VkAllocationCallbacks* a) {
        {std::lock_guard lock(self_->mutex_);
            for(auto s=self_->swaps_.begin();s!=self_->swaps_.end();) if(s->second.device==d){if(self_->selected_==s->first){self_->selected_=VK_NULL_HANDLE;self_->router_.release(RenderApi::Vulkan);}self_->swapWindows_.erase(s->first);s=self_->swaps_.erase(s);}else ++s;
            for(auto q=self_->queues_.begin();q!=self_->queues_.end();) if(q->second.device==d) q=self_->queues_.erase(q);else ++q;
            self_->devices_.erase(d);
        }self_->destroyDevice_(d,a);
    }
    static void VKAPI_CALL destroyInstance(VkInstance i,const VkAllocationCallbacks* a) {
        {std::lock_guard lock(self_->mutex_);for(auto p=self_->physical_.begin();p!=self_->physical_.end();) if(p->second==i) p=self_->physical_.erase(p);else ++p;
            if(self_->dispatchInstance_==i) self_->dispatchInstance_=VK_NULL_HANDLE;}
        self_->destroyInstance_(i,a);
    }
    static VkResult VKAPI_CALL present(VkQueue q,const VkPresentInfoKHR* info) {
        auto& self=*self_;std::lock_guard lock(self.mutex_);
        auto queue=self.queues_.find(q);
        if(!self.enabled_) return self.present_(q,info);
        if(auto active=self.router_.active();active!=RenderApi::Auto && active!=RenderApi::Vulkan) return self.present_(q,info);
        if(info->swapchainCount!=1 || queue==self.queues_.end()) {
            self.reportFailure("Vulkan presentation observed without captured device/queue context (or multiple swapchains). Late attachment is unsupported; recreate the renderer with hooks active.");
            return self.present_(q,info);
        }
        auto entry=self.swaps_.find(info->pSwapchains[0]);
        if(entry==self.swaps_.end()) {self.reportFailure("Vulkan swapchain predates hook attachment; its context cannot be recovered safely.");return self.present_(q,info);}
        if(entry->second.device!=queue->second.device || (self.selected_ && self.selected_!=entry->first)) return self.present_(q,info);
        auto& swap=entry->second;auto& device=self.devices_.at(swap.device);
        auto window=self.swapWindows_.find(entry->first);
        if(window==self.swapWindows_.end()) {self.reportFailure("Vulkan surface window was not captured; cannot safely select the game's swapchain.");return self.present_(q,info);}
        if(auto target=self.shared_.targetWindow.load();target && target!=reinterpret_cast<uintptr_t>(window->second)) return self.present_(q,info);
        if(!swap.safeOwnership || !(swap.usage&VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT) || swap.images.size()<2 || info->pImageIndices[0]>=swap.images.size()) {
            self.reportFailure("Vulkan swapchain usage, ownership or image configuration is unsupported.");return self.present_(q,info);
        }
        if(!swap.concurrentFamilies.empty() && std::find(swap.concurrentFamilies.begin(),swap.concurrentFamilies.end(),queue->second.family)==swap.concurrentFamilies.end()) return self.present_(q,info);
        // Keep partial/incremental presentation untouched until a pNext-aware region
        // compositor is implemented. The HUD otherwise falls outside dirty regions.
        for(auto* next=static_cast<const VkBaseInStructure*>(info->pNext);next;next=next->pNext)
            if(next->sType==VK_STRUCTURE_TYPE_PRESENT_REGIONS_KHR) return self.present_(q,info);
        VulkanFrame frame;
        try {
            if(!self.router_.tryActivate(RenderApi::Vulkan)) return self.present_(q,info);
            self.selected_=entry->first;
            if(!swap.renderer) {
                if(!swap.preparation.started()) {
                    VulkanBackendInfo context{device.instance,device.physical,swap.device,q,queue->second.family,static_cast<uint32_t>(swap.images.size()),std::max(2u,swap.minimum),swap.format};
                    swap.preparation.start([context]{
                        // Creates only privately owned device objects/pipeline, never
                        // submits to the application's externally synchronized queue.
                        return std::make_unique<RenderSession>(createVulkanBackend(context),std::make_unique<PreparedContext>());
                    });
                }
                swap.renderer=swap.preparation.take();if(!swap.renderer)return self.present_(q,info);
            }
            frame.image=swap.images[info->pImageIndices[0]];frame.extent=swap.extent;
            auto& signal=swap.signals[info->pImageIndices[0]];
            if(!signal){VkSemaphoreCreateInfo create{VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO};if(vkCreateSemaphore(swap.device,&create,nullptr,&signal)!=VK_SUCCESS)throw std::runtime_error("Cannot create Vulkan presentation semaphore");}
            frame.signal=signal;
            if(info->waitSemaphoreCount) frame.waits.assign(info->pWaitSemaphores,info->pWaitSemaphores+info->waitSemaphoreCount);
            Scene scene;std::shared_ptr<const ImageAssets> images;
            {std::lock_guard shared(self.shared_.mutex);scene=self.shared_.scene;images=self.shared_.images;}
            auto now=std::chrono::steady_clock::now();swap.renderer->render({swap.extent.width,swap.extent.height,std::chrono::duration<float>(now-swap.previous).count()},frame,scene,*images);swap.previous=now;
            if(swap.extent.width && swap.extent.height && frame.waitsConsumed && frame.completion==VK_SUCCESS) {
                auto& progress=self.shared_.progress;std::lock_guard status(progress.mutex);
                ++progress.submittedFrames;progress.window=reinterpret_cast<uintptr_t>(window->second);
                progress.lastFrameTick=GetTickCount64();progress.error.clear();
            }
        } catch(const std::exception& e){self.reportFailure(e.what());self.enabled_=false;}
        if(frame.waitsConsumed && frame.completion!=VK_SUCCESS){if(info->pResults) info->pResults[0]=frame.completion;return frame.completion;}
        auto forwarded=*info;
        if(frame.waitsConsumed){forwarded.waitSemaphoreCount=1;forwarded.pWaitSemaphores=&frame.signal;}
        return self.present_(q,&forwarded);
    }
    static PFN_vkVoidFunction intercept(const char* name,PFN_vkVoidFunction result) {
        if(!result) return result;
#define RETURN_WRAPPER(api, method, original) if(std::strcmp(name,#api)==0 && self_->original) return reinterpret_cast<PFN_vkVoidFunction>(&method)
        RETURN_WRAPPER(vkEnumeratePhysicalDevices,enumerate,enumerate_);
        RETURN_WRAPPER(vkCreateWin32SurfaceKHR,createSurface,createSurface_);
        RETURN_WRAPPER(vkDestroySurfaceKHR,destroySurface,destroySurface_);
        RETURN_WRAPPER(vkEnumeratePhysicalDeviceGroups,groups,groups_);
        RETURN_WRAPPER(vkCreateDevice,createDevice,createDevice_);RETURN_WRAPPER(vkDestroyDevice,destroyDevice,destroyDevice_);
        RETURN_WRAPPER(vkDestroyInstance,destroyInstance,destroyInstance_);
        RETURN_WRAPPER(vkGetDeviceQueue,getQueue,getQueue_);RETURN_WRAPPER(vkGetDeviceQueue2,getQueue2,getQueue2_);
        RETURN_WRAPPER(vkCreateSwapchainKHR,createSwap,createSwap_);RETURN_WRAPPER(vkDestroySwapchainKHR,destroySwap,destroySwap_);
        RETURN_WRAPPER(vkQueuePresentKHR,present,present_);RETURN_WRAPPER(vkGetDeviceProcAddr,getDeviceProc,gdpa_);
#undef RETURN_WRAPPER
        return result;
    }
    static PFN_vkVoidFunction VKAPI_CALL getInstanceProc(VkInstance i,const char* name){return intercept(name,self_->gipa_(i,name));}
    static PFN_vkVoidFunction VKAPI_CALL getDeviceProc(VkDevice d,const char* name){return intercept(name,self_->gdpa_(d,name));}
public:
    VulkanHook(SharedScene& s,RenderApiRouter& r):shared_(s),router_(r){}
    std::span<const RenderApi> apis() const noexcept override{static constexpr std::array apis{RenderApi::Vulkan};return apis;}
    void enable() noexcept override{enabled_=true;}
    void stop() noexcept override{enabled_=false;std::lock_guard lock(mutex_);clear();}
    void install() override {
        if(installed_) return;self_=this;
        if(volkInitialize()!=VK_SUCCESS) throw std::runtime_error("Vulkan loader unavailable");
        auto module=GetModuleHandleW(L"vulkan-1.dll");if(!module) throw std::runtime_error("Vulkan loader unavailable");
        auto result=MH_Initialize();if(result!=MH_OK && result!=MH_ERROR_ALREADY_INITIALIZED) throw std::runtime_error("MinHook initialization failed");
        std::vector<void*> addresses;
        auto hook=[&](const char* name,void* callback,void** original,bool optional=false){
            auto* address=reinterpret_cast<void*>(GetProcAddress(module,name));if(!address && optional) return;
            if(!address || MH_CreateHook(address,callback,original)!=MH_OK) throw std::runtime_error(std::string("Cannot hook ")+name);
            addresses.push_back(address);
        };
        try {
#define HOOK(api, method, original) hook(#api,reinterpret_cast<void*>(&method),reinterpret_cast<void**>(&original))
            HOOK(vkCreateWin32SurfaceKHR,createSurface,createSurface_);HOOK(vkDestroySurfaceKHR,destroySurface,destroySurface_);
            HOOK(vkEnumeratePhysicalDevices,enumerate,enumerate_);HOOK(vkCreateDevice,createDevice,createDevice_);HOOK(vkDestroyDevice,destroyDevice,destroyDevice_);HOOK(vkDestroyInstance,destroyInstance,destroyInstance_);
            HOOK(vkGetDeviceQueue,getQueue,getQueue_);HOOK(vkCreateSwapchainKHR,createSwap,createSwap_);HOOK(vkDestroySwapchainKHR,destroySwap,destroySwap_);HOOK(vkQueuePresentKHR,present,present_);
            hook("vkGetDeviceQueue2",reinterpret_cast<void*>(&getQueue2),reinterpret_cast<void**>(&getQueue2_),true);
            hook("vkEnumeratePhysicalDeviceGroups",reinterpret_cast<void*>(&groups),reinterpret_cast<void**>(&groups_),true);
            HOOK(vkGetInstanceProcAddr,getInstanceProc,gipa_);HOOK(vkGetDeviceProcAddr,getDeviceProc,gdpa_);
#undef HOOK
            for(auto address:addresses) if(MH_QueueEnableHook(address)!=MH_OK) throw std::runtime_error("Cannot queue Vulkan hook");
            if(MH_ApplyQueued()!=MH_OK) throw std::runtime_error("Cannot enable Vulkan hooks");installed_=true;
        } catch(...){for(auto address:addresses){MH_QueueDisableHook(address);MH_DisableHook(address);}throw;}
    }
};
}
std::unique_ptr<OverlayHook> createVulkanHook(SharedScene& s,RenderApiRouter& r){return std::make_unique<VulkanHook>(s,r);}
}
