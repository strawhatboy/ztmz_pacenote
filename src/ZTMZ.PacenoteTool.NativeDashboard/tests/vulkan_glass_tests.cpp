#include "graphics/vulkan/glass.hpp"
#include "graphics/render_session.hpp"
#include <windows.h>
#include <algorithm>
#include <cstring>
#include <iostream>
#include <stdexcept>
using namespace ztmz;
namespace {
void require(bool b,const char* s){if(!b)throw std::runtime_error(s);}
void check(VkResult r){if(r!=VK_SUCCESS&&r!=VK_SUBOPTIMAL_KHR)throw std::runtime_error("Vulkan call failed: "+std::to_string(r));}
unsigned validationErrors=0;
VKAPI_ATTR VkBool32 VKAPI_CALL debug(VkDebugUtilsMessageSeverityFlagBitsEXT severity,VkDebugUtilsMessageTypeFlagsEXT,
    const VkDebugUtilsMessengerCallbackDataEXT* data,void*) {
    if(severity&VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT){++validationErrors;std::cerr<<data->pMessage<<'\n';}return VK_FALSE;
}
struct Skip:std::runtime_error {using std::runtime_error::runtime_error;};
struct Fixture {
    VkInstance instance=VK_NULL_HANDLE;VkPhysicalDevice gpu=VK_NULL_HANDLE;VkDevice device=VK_NULL_HANDLE;
    VkQueue queue=VK_NULL_HANDLE;uint32_t family=0;HWND window=nullptr;VkSurfaceKHR surface=VK_NULL_HANDLE;
    VkSwapchainKHR swap=VK_NULL_HANDLE;VkExtent2D extent{};VkFormat format=VK_FORMAT_UNDEFINED;std::vector<VkImage> images;
    VkCommandPool pool=VK_NULL_HANDLE;VkCommandBuffer command=VK_NULL_HANDLE;
    VkSemaphore acquired=VK_NULL_HANDLE,sceneDone=VK_NULL_HANDLE,overlayDone=VK_NULL_HANDLE,readDone=VK_NULL_HANDLE;
    VkFence readFence=VK_NULL_HANDLE;VkDebugUtilsMessengerEXT messenger=VK_NULL_HANDLE;
    VkBuffer buffer=VK_NULL_HANDLE;VkDeviceMemory memory=VK_NULL_HANDLE;void* mapped=nullptr;
    VkImageUsageFlags usage=VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    std::vector<unsigned> background;
    uint32_t memoryType(uint32_t bits,VkMemoryPropertyFlags flags) {
        VkPhysicalDeviceMemoryProperties p{};vkGetPhysicalDeviceMemoryProperties(gpu,&p);
        for(uint32_t i=0;i<p.memoryTypeCount;++i)if((bits&(1u<<i))&&(p.memoryTypes[i].propertyFlags&flags)==flags)return i;
        throw std::runtime_error("No host coherent memory");
    }
    void init() {
        if(volkInitialize()!=VK_SUCCESS)throw Skip("Vulkan loader unavailable");
        uint32_t n=0;check(vkEnumerateInstanceLayerProperties(&n,nullptr));std::vector<VkLayerProperties> layers(n);check(vkEnumerateInstanceLayerProperties(&n,layers.data()));
        const char* validation="VK_LAYER_KHRONOS_validation";
        const bool hasValidation=std::any_of(layers.begin(),layers.end(),[&](const auto& l){return std::strcmp(l.layerName,validation)==0;});
        std::vector<const char*> extensions{VK_KHR_SURFACE_EXTENSION_NAME,VK_KHR_WIN32_SURFACE_EXTENSION_NAME};
        if(hasValidation)extensions.push_back(VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
        VkInstanceCreateInfo ic{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO};ic.enabledExtensionCount=uint32_t(extensions.size());ic.ppEnabledExtensionNames=extensions.data();
        if(hasValidation){ic.enabledLayerCount=1;ic.ppEnabledLayerNames=&validation;}
        check(vkCreateInstance(&ic,nullptr,&instance));volkLoadInstance(instance);
        if(hasValidation){VkDebugUtilsMessengerCreateInfoEXT m{VK_STRUCTURE_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT};m.messageSeverity=VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;m.messageType=VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT|VK_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT;m.pfnUserCallback=debug;check(vkCreateDebugUtilsMessengerEXT(instance,&m,nullptr,&messenger));}
        std::cout<<"Validation layer: "<<(hasValidation?"enabled":"unavailable")<<'\n';
        window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"Vulkan glass readback",WS_POPUP,-30000,-30000,640,360,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        require(window!=nullptr,"Test window creation failed");
        VkWin32SurfaceCreateInfoKHR sc{VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR};sc.hinstance=GetModuleHandleW(nullptr);sc.hwnd=window;check(vkCreateWin32SurfaceKHR(instance,&sc,nullptr,&surface));
        check(vkEnumeratePhysicalDevices(instance,&n,nullptr));if(!n)throw Skip("No Vulkan GPU");
        std::vector<VkPhysicalDevice> devices(n);check(vkEnumeratePhysicalDevices(instance,&n,devices.data()));
        for(auto candidate:devices) {
            vkGetPhysicalDeviceQueueFamilyProperties(candidate,&n,nullptr);std::vector<VkQueueFamilyProperties> families(n);vkGetPhysicalDeviceQueueFamilyProperties(candidate,&n,families.data());
            for(uint32_t i=0;i<n;++i){VkBool32 supported=0;check(vkGetPhysicalDeviceSurfaceSupportKHR(candidate,i,surface,&supported));if(supported&&(families[i].queueFlags&VK_QUEUE_GRAPHICS_BIT)){gpu=candidate;family=i;break;}}
            if(gpu)break;
        }
        if(!gpu)throw Skip("No graphics/present queue");
        VkPhysicalDeviceProperties properties{};vkGetPhysicalDeviceProperties(gpu,&properties);std::cout<<"GPU: "<<properties.deviceName<<'\n';
        float priority=1;VkDeviceQueueCreateInfo qc{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};qc.queueFamilyIndex=family;qc.queueCount=1;qc.pQueuePriorities=&priority;
        const char* extension=VK_KHR_SWAPCHAIN_EXTENSION_NAME;VkDeviceCreateInfo dc{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};dc.queueCreateInfoCount=1;dc.pQueueCreateInfos=&qc;dc.enabledExtensionCount=1;dc.ppEnabledExtensionNames=&extension;
        check(vkCreateDevice(gpu,&dc,nullptr,&device));vkGetDeviceQueue(device,family,0,&queue);
        VkCommandPoolCreateInfo pc{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};pc.queueFamilyIndex=family;check(vkCreateCommandPool(device,&pc,nullptr,&pool));
        VkCommandBufferAllocateInfo ca{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};ca.commandPool=pool;ca.level=VK_COMMAND_BUFFER_LEVEL_PRIMARY;ca.commandBufferCount=1;check(vkAllocateCommandBuffers(device,&ca,&command));
        VkSemaphoreCreateInfo sem{VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO};for(auto* s:{&acquired,&sceneDone,&overlayDone,&readDone})check(vkCreateSemaphore(device,&sem,nullptr,s));
        VkFenceCreateInfo fc{VK_STRUCTURE_TYPE_FENCE_CREATE_INFO};check(vkCreateFence(device,&fc,nullptr,&readFence));
        resize(640,360);
    }
    void resize(unsigned w,unsigned h) {
        if(queue)check(vkQueueWaitIdle(queue)); // Test-only resize/CPU-readback synchronization.
        if(swap){vkDestroySwapchainKHR(device,swap,nullptr);swap=VK_NULL_HANDLE;}
        if(mapped){vkUnmapMemory(device,memory);mapped=nullptr;}
        if(buffer)vkDestroyBuffer(device,buffer,nullptr);if(memory)vkFreeMemory(device,memory,nullptr);buffer=VK_NULL_HANDLE;memory=VK_NULL_HANDLE;
        SetWindowPos(window,nullptr,-30000,-30000,int(w),int(h),SWP_NOACTIVATE|SWP_NOZORDER);
        VkSurfaceCapabilitiesKHR caps{};check(vkGetPhysicalDeviceSurfaceCapabilitiesKHR(gpu,surface,&caps));
        if((caps.supportedUsageFlags&usage)!=usage)throw Skip("Surface lacks transfer readback usage");
        uint32_t n=0;check(vkGetPhysicalDeviceSurfaceFormatsKHR(gpu,surface,&n,nullptr));std::vector<VkSurfaceFormatKHR> formats(n);check(vkGetPhysicalDeviceSurfaceFormatsKHR(gpu,surface,&n,formats.data()));
        auto found=std::find_if(formats.begin(),formats.end(),[](const auto& f){return f.format==VK_FORMAT_R8G8B8A8_UNORM||f.format==VK_FORMAT_B8G8R8A8_UNORM;});
        if(found==formats.end())throw Skip("Surface lacks 8-bit UNORM format for deterministic comparison");format=found->format;
        extent=caps.currentExtent.width!=UINT32_MAX?caps.currentExtent:VkExtent2D{w,h};
        VkSwapchainCreateInfoKHR create{VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR};create.surface=surface;create.minImageCount=std::max(2u,caps.minImageCount);if(caps.maxImageCount)create.minImageCount=std::min(create.minImageCount,caps.maxImageCount);
        create.imageFormat=format;create.imageColorSpace=found->colorSpace;create.imageExtent=extent;create.imageArrayLayers=1;create.imageUsage=usage;create.imageSharingMode=VK_SHARING_MODE_EXCLUSIVE;create.preTransform=caps.currentTransform;
        create.compositeAlpha=VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;create.presentMode=VK_PRESENT_MODE_FIFO_KHR;create.clipped=VK_FALSE;
        check(vkCreateSwapchainKHR(device,&create,nullptr,&swap));check(vkGetSwapchainImagesKHR(device,swap,&n,nullptr));images.resize(n);check(vkGetSwapchainImagesKHR(device,swap,&n,images.data()));
        background.resize(extent.width*extent.height);
        for(unsigned y=0;y<extent.height;++y)for(unsigned x=0;x<extent.width;++x){bool bright=(x/16+y/16)%2;unsigned r=bright?50+180*x/extent.width:25+75*x/extent.width,g=bright?60+140*y/extent.height:30+60*y/extent.height,b=bright?185:65;background[y*extent.width+x]=r|(g<<8)|(b<<16)|0xff000000u;}
        VkBufferCreateInfo bc{VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO};bc.size=background.size()*4;bc.usage=VK_BUFFER_USAGE_TRANSFER_SRC_BIT|VK_BUFFER_USAGE_TRANSFER_DST_BIT;check(vkCreateBuffer(device,&bc,nullptr,&buffer));
        VkMemoryRequirements req{};vkGetBufferMemoryRequirements(device,buffer,&req);VkMemoryAllocateInfo ma{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};ma.allocationSize=req.size;ma.memoryTypeIndex=memoryType(req.memoryTypeBits,VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT|VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);check(vkAllocateMemory(device,&ma,nullptr,&memory));check(vkBindBufferMemory(device,buffer,memory,0));check(vkMapMemory(device,memory,0,VK_WHOLE_SIZE,0,&mapped));
    }
    unsigned packed(unsigned rgba)const{return format==VK_FORMAT_B8G8R8A8_UNORM?(rgba&0xff00ff00)|((rgba&255)<<16)|((rgba>>16)&255):rgba;}
    void begin(){check(vkResetCommandPool(device,pool,0));VkCommandBufferBeginInfo b{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};check(vkBeginCommandBuffer(command,&b));}
    void transition(VkImage image,VkImageLayout old,VkImageLayout next,VkAccessFlags src,VkAccessFlags dst,VkPipelineStageFlags from,VkPipelineStageFlags to){
        VkImageMemoryBarrier b{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};b.oldLayout=old;b.newLayout=next;b.image=image;b.srcAccessMask=src;b.dstAccessMask=dst;b.srcQueueFamilyIndex=b.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;b.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};vkCmdPipelineBarrier(command,from,to,0,0,nullptr,0,nullptr,1,&b);
    }
    void submit(VkSemaphore wait,VkSemaphore signal,VkFence fence=VK_NULL_HANDLE){
        check(vkEndCommandBuffer(command));VkPipelineStageFlags stage=VK_PIPELINE_STAGE_TRANSFER_BIT;VkSubmitInfo s{VK_STRUCTURE_TYPE_SUBMIT_INFO};s.commandBufferCount=1;s.pCommandBuffers=&command;s.waitSemaphoreCount=1;s.pWaitSemaphores=&wait;s.pWaitDstStageMask=&stage;s.signalSemaphoreCount=1;s.pSignalSemaphores=&signal;check(vkQueueSubmit(queue,1,&s,fence));
    }
    std::vector<unsigned> render(RenderSession& renderer,const Scene& scene,GlassCaptureStats& stats,bool allowCapture=true,bool invalidate=false) {
        uint32_t index=0;check(vkAcquireNextImageKHR(device,swap,UINT64_MAX,acquired,VK_NULL_HANDLE,&index));
        auto* p=static_cast<unsigned*>(mapped);for(size_t i=0;i<background.size();++i)p[i]=packed(background[i]);
        begin();transition(images[index],VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,0,VK_ACCESS_TRANSFER_WRITE_BIT,VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT);
        VkBufferImageCopy copy{};copy.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};copy.imageExtent={extent.width,extent.height,1};vkCmdCopyBufferToImage(command,buffer,images[index],VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&copy);
        transition(images[index],VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,VK_ACCESS_TRANSFER_WRITE_BIT,0,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT);
        // Wait only in the test harness so this upload command pool can be reused.
        check(vkResetFences(device,1,&readFence));submit(acquired,sceneDone,readFence);check(vkWaitForFences(device,1,&readFence,VK_TRUE,UINT64_MAX));
        VulkanFrame frame;frame.image=images[index];frame.extent=extent;frame.imageUsage=allowCapture?usage:VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;frame.waits={sceneDone};frame.signal=overlayDone;
        auto warnings=renderer.render({extent.width,extent.height,1.f/60},frame,scene,{});require(warnings.empty(),"Renderer rejected a command");require(frame.waitsConsumed&&frame.completion==VK_SUCCESS,"Overlay did not submit");stats=frame.glassCapture;
        if(invalidate)renderer.invalidateDeviceObjects(); // Must wait for in-flight resources before retiring them.
        begin();transition(images[index],VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,0,VK_ACCESS_TRANSFER_READ_BIT,VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT);
        vkCmdCopyImageToBuffer(command,images[index],VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,buffer,1,&copy);
        transition(images[index],VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,VK_ACCESS_TRANSFER_READ_BIT,0,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT);
        VkBufferMemoryBarrier host{VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER};host.srcAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT;host.dstAccessMask=VK_ACCESS_HOST_READ_BIT;host.srcQueueFamilyIndex=host.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;host.buffer=buffer;host.size=VK_WHOLE_SIZE;
        vkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_HOST_BIT,0,0,nullptr,1,&host,0,nullptr);
        check(vkResetFences(device,1,&readFence));submit(overlayDone,readDone,readFence);check(vkWaitForFences(device,1,&readFence,VK_TRUE,UINT64_MAX));
        std::vector<unsigned> output(background.size());for(size_t i=0;i<output.size();++i)output[i]=packed(p[i]);
        VkPresentInfoKHR present{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};present.waitSemaphoreCount=1;present.pWaitSemaphores=&readDone;present.swapchainCount=1;present.pSwapchains=&swap;present.pImageIndices=&index;check(vkQueuePresentKHR(queue,&present));check(vkQueueWaitIdle(queue));
        return output;
    }
    VulkanBackendInfo info()const{return {instance,gpu,device,queue,family,uint32_t(images.size()),2,format};}
    ~Fixture(){if(device)vkDeviceWaitIdle(device);if(mapped)vkUnmapMemory(device,memory);if(buffer)vkDestroyBuffer(device,buffer,nullptr);if(memory)vkFreeMemory(device,memory,nullptr);if(pool)vkDestroyCommandPool(device,pool,nullptr);if(readFence)vkDestroyFence(device,readFence,nullptr);for(auto s:{acquired,sceneDone,overlayDone,readDone})if(s)vkDestroySemaphore(device,s,nullptr);if(swap)vkDestroySwapchainKHR(device,swap,nullptr);if(device)vkDestroyDevice(device,nullptr);if(surface)vkDestroySurfaceKHR(instance,surface,nullptr);if(messenger)vkDestroyDebugUtilsMessengerEXT(instance,messenger,nullptr);if(instance)vkDestroyInstance(instance,nullptr);if(window)DestroyWindow(window);}
};
Scene panel(float refraction=18,float blur=0) {
    Scene s;s.width=640;s.height=360;s.layers={{"test","dynamic",Json::array({
        {{"type","drawGlassPanel"},{"x",80},{"y",60},{"width",480},{"height",240},{"radius",40},{"refraction",refraction},{"blur",blur},{"color",0}}
    })}};return s;
}
unsigned difference(unsigned a,unsigned b){unsigned d=0;for(unsigned shift:{0u,8u,16u})d+=unsigned(std::abs(int((a>>shift)&255)-int((b>>shift)&255)));return d;}
void swapchainCreationTests() {
    constexpr VkImageUsageFlags color=VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT,transfer=VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    VkSwapchainCreateInfoKHR original{VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR};
    original.imageUsage=color;original.imageArrayLayers=1;original.presentMode=VK_PRESENT_MODE_FIFO_KHR;
    original.imageExtent={640,360};original.imageFormat=VK_FORMAT_B8G8R8A8_UNORM;
    std::vector<VkSwapchainCreateInfoKHR> calls;unsigned checks=0;VkImageUsageFlags actual=0;
    auto preflight=[&](const auto& request){++checks;require(request.imageUsage==(color|transfer),"Preflight missed augmented usage");return true;};
    // Initial optional failure can safely retry the exact original request.
    auto result=createGlassSwapchain(original,color|transfer,preflight,[&](const auto& request){
        calls.push_back(request);return calls.size()==1?VK_ERROR_INITIALIZATION_FAILED:VK_SUCCESS;
    },actual);
    require(result==VK_SUCCESS && calls.size()==2 && checks==1,"Initial fallback did not retry once");
    require(calls[0].imageUsage==(color|transfer) && calls[1].imageUsage==color && actual==color,"Fallback recorded requested instead of actual usage");
    require(original.imageUsage==color && original.oldSwapchain==VK_NULL_HANDLE,"Game request was mutated");
    // Successful ordinary replacements preserve capture after a resolution change.
    original.oldSwapchain=(VkSwapchainKHR)1;original.imageExtent={1280,720};calls.clear();checks=0;
    result=createGlassSwapchain(original,color|transfer,preflight,[&](const auto& request){calls.push_back(request);return VK_SUCCESS;},actual);
    require(result==VK_SUCCESS && calls.size()==1 && checks==1 && actual==(color|transfer),"Replacement lost transfer usage");
    require(calls[0].oldSwapchain==original.oldSwapchain && calls[0].imageExtent.width==1280,"Replacement lost game lifecycle parameters");
    // Model Vulkan retirement even on failure. Any second creation is forbidden,
    // including a null-handle retry while the old chain owns the native window.
    calls.clear();bool retired=false;
    result=createGlassSwapchain(original,color|transfer,preflight,[&](const auto& request){
        require(!retired,"Retired replacement was retried");require(request.oldSwapchain==original.oldSwapchain,"Retirement bypassed with null handle");
        retired=true;calls.push_back(request);return VK_ERROR_OUT_OF_DEVICE_MEMORY;
    },actual);
    require(result==VK_ERROR_OUT_OF_DEVICE_MEMORY && calls.size()==1 && actual==0,"Replacement failure was masked");
    // A rejected implied image format must leave creation entirely unchanged.
    calls.clear();
    result=createGlassSwapchain(original,color|transfer,[](const auto&){return false;},[&](const auto& request){calls.push_back(request);return VK_SUCCESS;},actual);
    require(result==VK_SUCCESS && calls.size()==1 && calls[0].imageUsage==color && actual==color,"Unsupported format was augmented");
    // Surface rejection must not call the image-format preflight or retry.
    calls.clear();checks=0;
    result=createGlassSwapchain(original,color,preflight,[&](const auto& request){calls.push_back(request);return VK_SUCCESS;},actual);
    require(result==VK_SUCCESS && checks==0 && calls.size()==1 && actual==color,"Surface permission ignored");
    // An unknown extension (including newer usage2) may override imageUsage.
    // Test it behind a recognized entry so the entire chain must be inspected.
    VkBaseInStructure unknown{VK_STRUCTURE_TYPE_MAX_ENUM,nullptr};
    VkImageFormatListCreateInfo known{VK_STRUCTURE_TYPE_IMAGE_FORMAT_LIST_CREATE_INFO};known.pNext=&unknown;
    original.pNext=&known;original.imageUsage=color|transfer;calls.clear();checks=0;
    result=createGlassSwapchain(original,color|transfer,preflight,[&](const auto& request){calls.push_back(request);return VK_SUCCESS;},actual);
    require(result==VK_SUCCESS && checks==0 && calls.size()==1 && actual==0,"Unknown pNext inferred ignored legacy usage");
    require(calls[0].pNext==original.pNext && calls[0].imageUsage==original.imageUsage,"Unknown extension chain was changed");
    known.pNext=nullptr;
    require(knownSwapchainUsage(original),"Recognized non-usage extension disabled usage");
    for(auto type:{VK_STRUCTURE_TYPE_DEVICE_GROUP_SWAPCHAIN_CREATE_INFO_KHR,VK_STRUCTURE_TYPE_SWAPCHAIN_COUNTER_CREATE_INFO_EXT,
        VK_STRUCTURE_TYPE_SWAPCHAIN_DISPLAY_NATIVE_HDR_CREATE_INFO_AMD,VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_BARRIER_CREATE_INFO_NV,
        VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_MODES_CREATE_INFO_EXT,VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_SCALING_CREATE_INFO_EXT,
        VK_STRUCTURE_TYPE_SWAPCHAIN_LATENCY_CREATE_INFO_NV,VK_STRUCTURE_TYPE_IMAGE_COMPRESSION_CONTROL_EXT}) {
        // Only sType/pNext are inspected by policy; these nodes never reach Vulkan.
        VkBaseInStructure extension{type,nullptr};original.pNext=&extension;original.imageUsage=color;checks=0;calls.clear();
        result=createGlassSwapchain(original,color|transfer,preflight,[&](const auto& request){calls.push_back(request);return VK_SUCCESS;},actual);
        require(result==VK_SUCCESS && checks==0 && calls.size()==1 && actual==color,"Known extension lost overlay or was augmented without capability queries");
        original.imageUsage=color|transfer;
        result=createGlassSwapchain(original,color|transfer,preflight,[](const auto&){return VK_SUCCESS;},actual);
        require(result==VK_SUCCESS && actual==(color|transfer),"Known extension lost explicit game copy permission");
    }
    original.pNext=nullptr;original.oldSwapchain=VK_NULL_HANDLE;calls.clear();checks=0;
    result=createGlassSwapchain(original,color|transfer,preflight,[&](const auto& request){calls.push_back(request);return VK_ERROR_DEVICE_LOST;},actual);
    require(result==VK_ERROR_DEVICE_LOST && calls.size()==1 && checks==0 && actual==0,"Unmodified request was retried");
    std::cout<<"Vulkan swapchain policy passed: replacement preflight, retirement/no-retry, initial fallback, unknown pNext.\n";
}
void run() {
    swapchainCreationTests();
    require(glassSwapchainUsage(VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT,0,0,1)==VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT,"Unsupported usage was augmented");
    require(!(glassSwapchainUsage(VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT,VK_IMAGE_USAGE_TRANSFER_SRC_BIT,VK_SWAPCHAIN_CREATE_PROTECTED_BIT_KHR,1)&VK_IMAGE_USAGE_TRANSFER_SRC_BIT),"Protected usage was augmented");
    require(glassSwapchainUsage(VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT,VK_IMAGE_USAGE_TRANSFER_SRC_BIT,0,1)&VK_IMAGE_USAGE_TRANSFER_SRC_BIT,"Capable usage not augmented");
    {
    Fixture f;f.init();
    {
        RenderSession renderer(createVulkanBackend(f.info()));GlassCaptureStats stats;
        // Callback-only draw lists must still execute the glass shader.
        auto zero=f.render(renderer,panel(0,0),stats);require(stats.copies==1,"Callback-only glass failed to capture");
        require(stats.width<f.extent.width&&stats.height<f.extent.height,"Capture ignored bounded panel region");
        auto refracted=f.render(renderer,panel(18,0),stats);size_t edgeChanged=0;
        for(unsigned y=70;y<290;++y)for(unsigned x=82;x<105;++x)if(difference(zero[y*f.extent.width+x],refracted[y*f.extent.width+x])>12)++edgeChanged;
        require(edgeChanged>100,"Refraction did not displace real backdrop pixels");
        auto blurred=f.render(renderer,panel(0,5),stats);size_t interiorChanged=0;
        for(unsigned y=110;y<250;++y)for(unsigned x=140;x<500;++x)if(difference(zero[y*f.extent.width+x],blurred[y*f.extent.width+x])>8)++interiorChanged;
        require(interiorChanged>500,"Blur did not sample neighbouring backdrop pixels");
        require(difference(zero[180*f.extent.width+320],f.background[180*f.extent.width+320])<=3,"Zero-effect interior changed the backdrop");
        require(refracted[10*f.extent.width+10]==f.background[10*f.extent.width+10],"Glass modified outside its panel");
        auto foreground=panel();foreground.layers[0].commands.push_back({{"type","fillRectangle"},{"x",270},{"y",140},{"width",100},{"height",50},{"color",0xff00ff00u}});
        auto fg=f.render(renderer,foreground,stats);require(difference(fg[160*f.extent.width+300],0xff00ff00u)<3,"Glass pipeline state leaked into foreground drawing");
        auto before=panel();before.layers[0].commands.insert(before.layers[0].commands.begin(),Json{{"type","fillRectangle"},{"x",200},{"y",120},{"width",100},{"height",60},{"color",0xffff0000u}});
        auto b=f.render(renderer,before,stats);require(difference(b[150*f.extent.width+250],f.background[150*f.extent.width+250])<3,"Snapshot captured preceding HUD instead of game backdrop");
        auto multiple=panel();multiple.layers[0].commands.push_back({{"type","drawGlassPanel"},{"x",10},{"y",10},{"width",50},{"height",40},{"radius",8},{"refraction",5},{"blur",2},{"color",0}});
        f.render(renderer,multiple,stats);require(stats.copies==1,"Multiple panels caused multiple copies");
        auto fallback=foreground;fallback.layers[0].commands[0]["color"]=0xff334455u;
        auto fallbackPixels=f.render(renderer,fallback,stats,false);require(stats.copies==0,"Unsupported image usage was read");
        require(difference(fallbackPixels[120*f.extent.width+220],0xff554433u)<3,"Fallback tint disappeared");
        require(difference(fallbackPixels[160*f.extent.width+300],0xff00ff00u)<3,"Fallback foreground disappeared");
        auto off=panel();off.layers[0].commands[0]["x"]=-1000;f.render(renderer,off,stats);require(stats.copies==0,"Offscreen glass captured the scene");
        auto none=panel();none.layers[0].commands=Json::array();f.render(renderer,none,stats);require(stats.copies==0,"Glass-off frame copied stale scene");
        auto clipped=panel();clipped.layers[0].commands.insert(clipped.layers[0].commands.begin(),Json{{"type","clipRegionStart"},{"x",150},{"y",100},{"width",100},{"height",80}});clipped.layers[0].commands.push_back({{"type","clipRegionEnd"}});
        auto c=f.render(renderer,clipped,stats);require(stats.copies==1&&stats.width<=140&&stats.height<=120,"Capture ignored clip bounds");require(c[80*f.extent.width+90]==f.background[80*f.extent.width+90],"Glass ignored clip bounds");
        for(int i=0;i<3;++i){f.render(renderer,panel(),stats,true,true);require(stats.copies==1,"Invalidate/recreate lost glass");}
        renderer.invalidateDeviceObjects(); // Release the borrowed swapchain view before destroying its image.
        f.resize(480,270);f.render(renderer,panel(),stats);require(stats.copies==1,"Resize lost glass");
    }
    // Device-owned resources are retired with their submission fence on teardown.
    {RenderSession restarted(createVulkanBackend(f.info()));GlassCaptureStats stats;f.render(restarted,panel(),stats);require(stats.copies==1,"Restart lost glass");}
    }
    require(validationErrors==0,"Vulkan validation errors");
    std::cout<<"Vulkan glass passed: callback-only GPU refraction/blur, foreground state, pre-HUD capture, one bounded copy, usage/clip/off fallbacks, resize/invalidation/restart.\n";
}
}
int main(){try{run();return 0;}catch(const Skip& e){std::cout<<"SKIP: "<<e.what()<<'\n';return 77;}catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}}
