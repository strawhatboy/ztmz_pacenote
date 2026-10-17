#pragma once
#include "graphics/render_backend.hpp"
#include "graphics/glass_capture.hpp"
#include <volk.h>
namespace ztmz {
struct VulkanBackendInfo {
    VkInstance instance=VK_NULL_HANDLE;
    VkPhysicalDevice physicalDevice=VK_NULL_HANDLE;
    VkDevice device=VK_NULL_HANDLE;
    VkQueue queue=VK_NULL_HANDLE;
    uint32_t queueFamily=0,imageCount=0,minImageCount=2;
    VkFormat format=VK_FORMAT_UNDEFINED;
};
struct VulkanFrame final:NativeFrame {
    RenderApi api() const noexcept override{return RenderApi::Vulkan;}
    VkImage image=VK_NULL_HANDLE;
    VkExtent2D extent{};
    VkImageUsageFlags imageUsage=0; // Captured at swapchain creation; never inferred.
    GlassCaptureStats glassCapture{};
    std::vector<VkSemaphore> waits;
    // Owned per swap-chain image by the hook, surviving overlay stop/restart.
    // Present waits on this semaphore instead of forcing a CPU fence wait.
    VkSemaphore signal=VK_NULL_HANDLE;
    bool waitsConsumed=false;
    VkResult completion=VK_SUCCESS;
};
std::unique_ptr<RenderBackend> createVulkanBackend(const VulkanBackendInfo&);
}
