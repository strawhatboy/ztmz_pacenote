#pragma once
#include "graphics/vulkan/backend.hpp"
#include "graphics/vulkan/swapchain.hpp"
namespace ztmz {
// All mutations occur after the backend fence completes. Capture and draw are
// recorded into its single command buffer; this object never submits or waits.
class VulkanGlass {
    VulkanBackendInfo info_;
    VkRenderPass renderPass_;
    VkDescriptorSetLayout setLayout_=VK_NULL_HANDLE;
    VkPipelineLayout layout_=VK_NULL_HANDLE;
    VkPipeline pipeline_=VK_NULL_HANDLE;
    VkDescriptorPool pool_=VK_NULL_HANDLE;
    VkDescriptorSet set_=VK_NULL_HANDLE;
    VkSampler sampler_=VK_NULL_HANDLE;
    VkImage snapshot_=VK_NULL_HANDLE,source_=VK_NULL_HANDLE;
    VkImageView view_=VK_NULL_HANDLE;
    VkDeviceMemory memory_=VK_NULL_HANDLE;
    VkExtent2D extent_{};
    VkRect2D region_{};
    bool attempted_=false,initialized_=false,hasRegion_=false;
    bool initialize();
    bool resize(VkExtent2D);
    void releaseSnapshot();
    void drawPanel(const GlassPanel&,const ImDrawCmd&);
public:
    VulkanGlass(const VulkanBackendInfo& info,VkRenderPass pass):info_(info),renderPass_(pass){}
    ~VulkanGlass();
    void beginFrame(){source_=VK_NULL_HANDLE;hasRegion_=false;}
    GlassDraw prepare(const VulkanFrame&);
    GlassCaptureStats capture(VkCommandBuffer);
};
}
