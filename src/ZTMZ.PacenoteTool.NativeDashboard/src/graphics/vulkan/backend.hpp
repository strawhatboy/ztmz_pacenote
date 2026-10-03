#pragma once
#include "graphics/render_backend.hpp"
#include <vulkan/vulkan.h>
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
    std::vector<VkSemaphore> waits;
    // This first implementation completes its overlay submission on a CPU fence.
    // The hook then forwards presentation with no waits: original waits were consumed once.
    bool waitsConsumed=false;
    VkResult completion=VK_SUCCESS;
};
std::unique_ptr<RenderBackend> createVulkanBackend(const VulkanBackendInfo&);
}
