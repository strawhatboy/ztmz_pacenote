#include <windows.h>
#include <volk.h>
#include <nlohmann/json.hpp>
#include <algorithm>
#include <chrono>
#include <iostream>
#include <stdexcept>
#include <vector>
void require(bool ok,const char* text){if(!ok)throw std::runtime_error(text);}
void check(VkResult result){require(result==VK_SUCCESS||result==VK_SUBOPTIMAL_KHR,"Vulkan call failed");}
nlohmann::json status() {
    auto name=L"\\\\.\\pipe\\ztmz-dashboard-"+std::to_wstring(GetCurrentProcessId());
    require(WaitNamedPipeW(name.c_str(),3000)!=FALSE,"Control pipe timeout");
    auto pipe=CreateFileW(name.c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);
    require(pipe!=INVALID_HANDLE_VALUE,"Control pipe");
    struct Close{HANDLE h;~Close(){CloseHandle(h);}} close{pipe};
    DWORD mode=PIPE_READMODE_MESSAGE,n=0;SetNamedPipeHandleState(pipe,&mode,nullptr,nullptr);
    std::string request=R"({"version":1,"command":"status"})";
    require(WriteFile(pipe,request.data(),static_cast<DWORD>(request.size()),&n,nullptr)!=FALSE,"Control write");
    char buffer[4096];require(ReadFile(pipe,buffer,sizeof(buffer),&n,nullptr)!=FALSE,"Control read");
    auto result=nlohmann::json::parse(buffer,buffer+n);char ack=1;WriteFile(pipe,&ack,1,&n,nullptr);return result;
}
int wmain(int argc,wchar_t** argv) {
    try {
        require(argc==2,"Expected fixture DLL");SetEnvironmentVariableW(L"ZTMZ_RENDER_API",L"vulkan");
        auto dll=LoadLibraryExW(argv[1],nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);require(dll!=nullptr,"Fixture DLL");
        auto start=reinterpret_cast<BOOL(WINAPI*)(USHORT)>(GetProcAddress(dll,"ZtmzDashboardStart"));
        auto stop=reinterpret_cast<void(WINAPI*)()>(GetProcAddress(dll,"ZtmzDashboardStop"));
        auto scene=reinterpret_cast<void(WINAPI*)()>(GetProcAddress(dll,"ZtmzDashboardTestScene"));
        auto textures=reinterpret_cast<void(WINAPI*)(UINT)>(GetProcAddress(dll,"ZtmzDashboardTestTextures"));
        require(start&&stop&&scene&&textures,"Exports");scene();require(start(8080),"Early hook start");
        check(volkInitialize());
        VkInstance instance;const char* extensions[]{VK_KHR_SURFACE_EXTENSION_NAME,VK_KHR_WIN32_SURFACE_EXTENSION_NAME};
        VkInstanceCreateInfo ic{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO};ic.enabledExtensionCount=2;ic.ppEnabledExtensionNames=extensions;
        check(vkCreateInstance(&ic,nullptr,&instance));volkLoadInstance(instance);
        auto window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"Vulkan overlay test",WS_POPUP|WS_VISIBLE,-30000,-30000,640,360,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        require(window!=nullptr,"Window");
        VkWin32SurfaceCreateInfoKHR sc{VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR};sc.hinstance=GetModuleHandleW(nullptr);sc.hwnd=window;
        VkSurfaceKHR surface;check(vkCreateWin32SurfaceKHR(instance,&sc,nullptr,&surface));
        uint32_t count=0;check(vkEnumeratePhysicalDevices(instance,&count,nullptr));require(count>0,"No Vulkan GPU");
        std::vector<VkPhysicalDevice> physical(count);check(vkEnumeratePhysicalDevices(instance,&count,physical.data()));
        auto gpu=physical[0];vkGetPhysicalDeviceQueueFamilyProperties(gpu,&count,nullptr);
        std::vector<VkQueueFamilyProperties> families(count);vkGetPhysicalDeviceQueueFamilyProperties(gpu,&count,families.data());
        uint32_t family=0;for(;family<count;++family){VkBool32 supported=0;check(vkGetPhysicalDeviceSurfaceSupportKHR(gpu,family,surface,&supported));if(supported&&(families[family].queueFlags&VK_QUEUE_GRAPHICS_BIT))break;}
        require(family<count,"No graphics/present queue");
        float priority=1;VkDeviceQueueCreateInfo qc{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};qc.queueFamilyIndex=family;qc.queueCount=1;qc.pQueuePriorities=&priority;
        const char* swapExtension=VK_KHR_SWAPCHAIN_EXTENSION_NAME;VkDeviceCreateInfo dc{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};dc.queueCreateInfoCount=1;dc.pQueueCreateInfos=&qc;dc.enabledExtensionCount=1;dc.ppEnabledExtensionNames=&swapExtension;
        VkDevice device;check(vkCreateDevice(gpu,&dc,nullptr,&device));
        VkQueue queue;vkGetDeviceQueue(device,family,0,&queue);
        VkSurfaceCapabilitiesKHR caps{};check(vkGetPhysicalDeviceSurfaceCapabilitiesKHR(gpu,surface,&caps));
        check(vkGetPhysicalDeviceSurfaceFormatsKHR(gpu,surface,&count,nullptr));std::vector<VkSurfaceFormatKHR> formats(count);check(vkGetPhysicalDeviceSurfaceFormatsKHR(gpu,surface,&count,formats.data()));
        VkSwapchainCreateInfoKHR create{VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR};create.surface=surface;create.minImageCount=std::max(2u,caps.minImageCount);
        if(caps.maxImageCount)create.minImageCount=std::min(create.minImageCount,caps.maxImageCount);
        create.imageFormat=formats[0].format;create.imageColorSpace=formats[0].colorSpace;create.imageExtent=caps.currentExtent.width==UINT32_MAX?VkExtent2D{640,360}:caps.currentExtent;
        create.imageArrayLayers=1;create.imageUsage=VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT;
        create.imageSharingMode=VK_SHARING_MODE_EXCLUSIVE;create.preTransform=caps.currentTransform;create.compositeAlpha=VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;create.presentMode=VK_PRESENT_MODE_FIFO_KHR;create.clipped=VK_TRUE;
        VkSwapchainKHR swap;check(vkCreateSwapchainKHR(device,&create,nullptr,&swap));
        std::vector<VkImage> images;auto getImages=[&]{check(vkGetSwapchainImagesKHR(device,swap,&count,nullptr));images.resize(count);check(vkGetSwapchainImagesKHR(device,swap,&count,images.data()));};getImages();
        VkCommandPoolCreateInfo pc{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};pc.queueFamilyIndex=family;
        VkCommandPool pool;check(vkCreateCommandPool(device,&pc,nullptr,&pool));
        VkCommandBufferAllocateInfo ac{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};ac.commandPool=pool;ac.level=VK_COMMAND_BUFFER_LEVEL_PRIMARY;ac.commandBufferCount=1;
        VkCommandBuffer command;check(vkAllocateCommandBuffers(device,&ac,&command));
        VkSemaphoreCreateInfo sem{VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO};VkSemaphore acquired,finished;check(vkCreateSemaphore(device,&sem,nullptr,&acquired));check(vkCreateSemaphore(device,&sem,nullptr,&finished));
        double maximum=0;
        auto frame=[&]{
            uint32_t index=0;check(vkAcquireNextImageKHR(device,swap,UINT64_MAX,acquired,VK_NULL_HANDLE,&index));
            check(vkResetCommandPool(device,pool,0));VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};check(vkBeginCommandBuffer(command,&begin));
            VkImageMemoryBarrier barrier{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};barrier.srcQueueFamilyIndex=barrier.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;
            barrier.image=images[index];barrier.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};barrier.oldLayout=VK_IMAGE_LAYOUT_UNDEFINED;barrier.newLayout=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;barrier.dstAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT;
            vkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT,0,0,nullptr,0,nullptr,1,&barrier);
            VkClearColorValue blue{{0,0,1,1}};vkCmdClearColorImage(command,images[index],VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,&blue,1,&barrier.subresourceRange);
            barrier.oldLayout=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;barrier.newLayout=VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;barrier.srcAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT;barrier.dstAccessMask=0;
            vkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,0,0,nullptr,0,nullptr,1,&barrier);check(vkEndCommandBuffer(command));
            VkPipelineStageFlags stage=VK_PIPELINE_STAGE_TRANSFER_BIT;VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO};submit.waitSemaphoreCount=1;submit.pWaitSemaphores=&acquired;submit.pWaitDstStageMask=&stage;submit.commandBufferCount=1;submit.pCommandBuffers=&command;submit.signalSemaphoreCount=1;submit.pSignalSemaphores=&finished;
            check(vkQueueSubmit(queue,1,&submit,VK_NULL_HANDLE));
            VkPresentInfoKHR present{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};present.waitSemaphoreCount=1;present.pWaitSemaphores=&finished;present.swapchainCount=1;present.pSwapchains=&swap;present.pImageIndices=&index;
            auto before=std::chrono::steady_clock::now();check(vkQueuePresentKHR(queue,&present));
            maximum=std::max(maximum,std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-before).count());
            check(vkQueueWaitIdle(queue)); // test read/teardown synchronization, outside measured Present
        };
        for(int cycle=0;cycle<3;++cycle){
            require(start(8080),"Restart");auto deadline=GetTickCount64()+10000;
            do{frame();auto s=status();require(s.value("error",std::string{}).empty(),"Hook error");if(s["submittedFrames"].get<uint64_t>()>0)break;require(GetTickCount64()<deadline,"No Vulkan submission");Sleep(1);}while(true);
            textures(cycle);for(int n=0;n<20;++n)frame();
            stop();frame();require(status()["submittedFrames"]==0,"Stop retained frame progress");
            vkDestroySwapchainKHR(device,swap,nullptr);check(vkCreateSwapchainKHR(device,&create,nullptr,&swap));getImages();
        }
        vkDestroySemaphore(device,acquired,nullptr);vkDestroySemaphore(device,finished,nullptr);vkDestroyCommandPool(device,pool,nullptr);
        vkDestroySwapchainKHR(device,swap,nullptr);vkDestroyDevice(device,nullptr);vkDestroySurfaceKHR(instance,surface,nullptr);vkDestroyInstance(instance,nullptr);DestroyWindow(window);
        std::cout<<"Vulkan early attachment, asynchronous startup/uploads, semaphore presentation, restart and swapchain recreation passed; max Present "<<maximum<<" ms (includes driver presentation).\n";return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
