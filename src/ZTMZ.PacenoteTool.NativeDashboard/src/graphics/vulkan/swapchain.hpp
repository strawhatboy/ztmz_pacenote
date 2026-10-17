#pragma once
#include <volk.h>
namespace ztmz {
// These extensions do not override imageUsage. Unknown extensions may include
// VkImageUsageFlags2CreateInfoKHR, absent from our pinned headers: never infer
// either transfer or color-attachment permission from the ignored legacy field.
inline bool knownSwapchainUsage(const VkSwapchainCreateInfoKHR& info,bool forAugmentation=false) {
    for(auto* p=static_cast<const VkBaseInStructure*>(info.pNext);p;p=p->pNext) {
        switch(p->sType) {
        case VK_STRUCTURE_TYPE_IMAGE_FORMAT_LIST_CREATE_INFO:
        case VK_STRUCTURE_TYPE_SURFACE_FULL_SCREEN_EXCLUSIVE_INFO_EXT:
        case VK_STRUCTURE_TYPE_SURFACE_FULL_SCREEN_EXCLUSIVE_WIN32_INFO_EXT:
            break;
        case VK_STRUCTURE_TYPE_DEVICE_GROUP_SWAPCHAIN_CREATE_INFO_KHR:
        case VK_STRUCTURE_TYPE_SWAPCHAIN_COUNTER_CREATE_INFO_EXT:
        case VK_STRUCTURE_TYPE_SWAPCHAIN_DISPLAY_NATIVE_HDR_CREATE_INFO_AMD:
        case VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_BARRIER_CREATE_INFO_NV:
        case VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_MODES_CREATE_INFO_EXT:
        case VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_SCALING_CREATE_INFO_EXT:
        case VK_STRUCTURE_TYPE_SWAPCHAIN_LATENCY_CREATE_INFO_NV:
        case VK_STRUCTURE_TYPE_IMAGE_COMPRESSION_CONTROL_EXT:
            // Preserve existing permissions, but do not add usage without
            // extension-specific surface/image capability queries.
            if(forAugmentation)return false;
            break;
        default:return false;
        }
    }
    return true;
}
inline VkImageUsageFlags glassSwapchainUsage(VkImageUsageFlags requested,VkImageUsageFlags supported,
    VkSwapchainCreateFlagsKHR flags,uint32_t layers) {
    return layers==1 && flags==0
        && (requested&VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT) && (supported&VK_IMAGE_USAGE_TRANSFER_SRC_BIT)
        ? requested|VK_IMAGE_USAGE_TRANSFER_SRC_BIT : requested;
}
// Injected callables make retirement/failure behavior testable without asking a
// driver to fail creation. FormatSupported checks the implied image parameters.
template<class FormatSupported,class Create>
VkResult createGlassSwapchain(const VkSwapchainCreateInfoKHR& original,VkImageUsageFlags supported,
    FormatSupported&& formatSupported,Create&& create,VkImageUsageFlags& actualUsage) {
    actualUsage=0;
    const bool known=knownSwapchainUsage(original);
    auto request=original;
    const bool ordinary=original.presentMode==VK_PRESENT_MODE_FIFO_KHR || original.presentMode==VK_PRESENT_MODE_FIFO_RELAXED_KHR
        || original.presentMode==VK_PRESENT_MODE_MAILBOX_KHR || original.presentMode==VK_PRESENT_MODE_IMMEDIATE_KHR;
    if(known && ordinary && knownSwapchainUsage(original,true)) {
        request.imageUsage=glassSwapchainUsage(original.imageUsage,supported,original.flags,original.imageArrayLayers);
        if(request.imageUsage!=original.imageUsage && !formatSupported(request))request.imageUsage=original.imageUsage;
    }
    auto result=create(request);
    if(result!=VK_SUCCESS && original.oldSwapchain==VK_NULL_HANDLE && request.imageUsage!=original.imageUsage) {
        request=original;result=create(request);
    }
    // A non-null oldSwapchain is retired even on failure. Never retry with it;
    // NULL is not a workaround while the window still has an associated chain.
    if(result==VK_SUCCESS && known)actualUsage=request.imageUsage;
    return result;
}
}
