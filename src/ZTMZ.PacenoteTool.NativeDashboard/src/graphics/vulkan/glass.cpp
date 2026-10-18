#include "graphics/vulkan/glass.hpp"
#include "vulkan_glass_shaders.hpp"
#include <imgui_impl_vulkan.h>
#include <algorithm>
#include <cmath>
namespace ztmz {
namespace {
VkImageMemoryBarrier barrier(VkImage image,VkImageLayout before,VkImageLayout after,
    VkAccessFlags src,VkAccessFlags dst) {
    VkImageMemoryBarrier b{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};
    b.oldLayout=before;b.newLayout=after;b.srcAccessMask=src;b.dstAccessMask=dst;
    b.srcQueueFamilyIndex=b.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;
    b.image=image;b.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};return b;
}
bool ordinaryFormat(VkFormat f) {
    return f==VK_FORMAT_R8G8B8A8_UNORM || f==VK_FORMAT_R8G8B8A8_SRGB ||
        f==VK_FORMAT_B8G8R8A8_UNORM || f==VK_FORMAT_B8G8R8A8_SRGB ||
        f==VK_FORMAT_A2B10G10R10_UNORM_PACK32 || f==VK_FORMAT_A2R10G10B10_UNORM_PACK32 ||
        f==VK_FORMAT_R16G16B16A16_SFLOAT;
}
}
bool VulkanGlass::initialize() {
    if(attempted_)return initialized_;
    attempted_=true;
    if(!ordinaryFormat(info_.format))return false;
    VkFormatProperties properties{};vkGetPhysicalDeviceFormatProperties(info_.physicalDevice,info_.format,&properties);
    const auto required=VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT|VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT;
    if((properties.optimalTilingFeatures&required)!=required)return false;
    VkDescriptorSetLayoutBinding bindings[2]{};
    bindings[0]={0,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,1,VK_SHADER_STAGE_FRAGMENT_BIT,nullptr};
    bindings[1]={1,VK_DESCRIPTOR_TYPE_SAMPLER,1,VK_SHADER_STAGE_FRAGMENT_BIT,nullptr};
    VkDescriptorSetLayoutCreateInfo set{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};set.bindingCount=2;set.pBindings=bindings;
    if(vkCreateDescriptorSetLayout(info_.device,&set,nullptr,&setLayout_)!=VK_SUCCESS)return false;
    VkPushConstantRange push{VK_SHADER_STAGE_FRAGMENT_BIT,0,128};
    VkPipelineLayoutCreateInfo layout{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};layout.setLayoutCount=1;layout.pSetLayouts=&setLayout_;layout.pushConstantRangeCount=1;layout.pPushConstantRanges=&push;
    if(vkCreatePipelineLayout(info_.device,&layout,nullptr,&layout_)!=VK_SUCCESS)return false;
    VkDescriptorPoolSize sizes[]={{VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,1},{VK_DESCRIPTOR_TYPE_SAMPLER,1}};
    VkDescriptorPoolCreateInfo pool{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};pool.maxSets=1;pool.poolSizeCount=2;pool.pPoolSizes=sizes;
    if(vkCreateDescriptorPool(info_.device,&pool,nullptr,&pool_)!=VK_SUCCESS)return false;
    VkDescriptorSetAllocateInfo allocation{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};allocation.descriptorPool=pool_;allocation.descriptorSetCount=1;allocation.pSetLayouts=&setLayout_;
    if(vkAllocateDescriptorSets(info_.device,&allocation,&set_)!=VK_SUCCESS)return false;
    VkSamplerCreateInfo sampler{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO};sampler.magFilter=sampler.minFilter=VK_FILTER_LINEAR;
    sampler.mipmapMode=VK_SAMPLER_MIPMAP_MODE_NEAREST;sampler.addressModeU=sampler.addressModeV=sampler.addressModeW=VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    if(vkCreateSampler(info_.device,&sampler,nullptr,&sampler_)!=VK_SUCCESS)return false;
    VkShaderModule vs=VK_NULL_HANDLE,ps=VK_NULL_HANDLE;
    struct Modules {VkDevice d;VkShaderModule& vs;VkShaderModule& ps;~Modules(){if(vs)vkDestroyShaderModule(d,vs,nullptr);if(ps)vkDestroyShaderModule(d,ps,nullptr);}} modules{info_.device,vs,ps};
    VkShaderModuleCreateInfo shader{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO};shader.codeSize=sizeof(vulkan_glass_vs);shader.pCode=vulkan_glass_vs;
    if(vkCreateShaderModule(info_.device,&shader,nullptr,&vs)!=VK_SUCCESS)return false;
    shader.codeSize=sizeof(vulkan_glass_ps);shader.pCode=vulkan_glass_ps;
    if(vkCreateShaderModule(info_.device,&shader,nullptr,&ps)!=VK_SUCCESS)return false;
    VkPipelineShaderStageCreateInfo stages[2]{};
    stages[0]={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_VERTEX_BIT,vs,"vsMain",nullptr};
    stages[1]={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_FRAGMENT_BIT,ps,"psMain",nullptr};
    VkPipelineVertexInputStateCreateInfo vertex{VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO};
    VkPipelineInputAssemblyStateCreateInfo assembly{VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO};assembly.topology=VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
    VkPipelineViewportStateCreateInfo viewport{VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO};viewport.viewportCount=viewport.scissorCount=1;
    VkPipelineRasterizationStateCreateInfo raster{VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO};raster.polygonMode=VK_POLYGON_MODE_FILL;raster.cullMode=VK_CULL_MODE_NONE;raster.frontFace=VK_FRONT_FACE_COUNTER_CLOCKWISE;raster.lineWidth=1;
    VkPipelineMultisampleStateCreateInfo samples{VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO};samples.rasterizationSamples=VK_SAMPLE_COUNT_1_BIT;
    VkPipelineColorBlendAttachmentState blend{};blend.blendEnable=VK_TRUE;blend.srcColorBlendFactor=VK_BLEND_FACTOR_SRC_ALPHA;blend.dstColorBlendFactor=VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;blend.colorBlendOp=VK_BLEND_OP_ADD;
    blend.srcAlphaBlendFactor=VK_BLEND_FACTOR_ONE;blend.dstAlphaBlendFactor=VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;blend.alphaBlendOp=VK_BLEND_OP_ADD;blend.colorWriteMask=15;
    VkPipelineColorBlendStateCreateInfo blending{VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO};blending.attachmentCount=1;blending.pAttachments=&blend;
    VkDynamicState states[]={VK_DYNAMIC_STATE_VIEWPORT,VK_DYNAMIC_STATE_SCISSOR};
    VkPipelineDynamicStateCreateInfo dynamic{VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO};dynamic.dynamicStateCount=2;dynamic.pDynamicStates=states;
    VkGraphicsPipelineCreateInfo pipeline{VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO};pipeline.stageCount=2;pipeline.pStages=stages;
    pipeline.pVertexInputState=&vertex;pipeline.pInputAssemblyState=&assembly;pipeline.pViewportState=&viewport;pipeline.pRasterizationState=&raster;pipeline.pMultisampleState=&samples;pipeline.pColorBlendState=&blending;pipeline.pDynamicState=&dynamic;pipeline.layout=layout_;pipeline.renderPass=renderPass_;
    initialized_=vkCreateGraphicsPipelines(info_.device,VK_NULL_HANDLE,1,&pipeline,nullptr,&pipeline_)==VK_SUCCESS;
    return initialized_;
}
void VulkanGlass::releaseSnapshot() {
    if(view_)vkDestroyImageView(info_.device,view_,nullptr);
    if(snapshot_)vkDestroyImage(info_.device,snapshot_,nullptr);
    if(memory_)vkFreeMemory(info_.device,memory_,nullptr);
    view_=VK_NULL_HANDLE;snapshot_=VK_NULL_HANDLE;memory_=VK_NULL_HANDLE;extent_={};
}
bool VulkanGlass::resize(VkExtent2D extent) {
    if(view_&&extent.width==extent_.width&&extent.height==extent_.height)return true;
    releaseSnapshot();
    VkImageCreateInfo image{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};image.imageType=VK_IMAGE_TYPE_2D;image.format=info_.format;
    image.extent={extent.width,extent.height,1};image.mipLevels=image.arrayLayers=1;image.samples=VK_SAMPLE_COUNT_1_BIT;image.tiling=VK_IMAGE_TILING_OPTIMAL;image.usage=VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT;
    if(vkCreateImage(info_.device,&image,nullptr,&snapshot_)!=VK_SUCCESS)return false;
    VkMemoryRequirements requirements{};vkGetImageMemoryRequirements(info_.device,snapshot_,&requirements);
    VkPhysicalDeviceMemoryProperties properties{};vkGetPhysicalDeviceMemoryProperties(info_.physicalDevice,&properties);
    uint32_t type=0;for(;type<properties.memoryTypeCount;++type)
        if((requirements.memoryTypeBits&(1u<<type)) && (properties.memoryTypes[type].propertyFlags&VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT))break;
    if(type==properties.memoryTypeCount)return false;
    VkMemoryAllocateInfo allocation{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};allocation.allocationSize=requirements.size;allocation.memoryTypeIndex=type;
    if(vkAllocateMemory(info_.device,&allocation,nullptr,&memory_)!=VK_SUCCESS || vkBindImageMemory(info_.device,snapshot_,memory_,0)!=VK_SUCCESS)return false;
    VkImageViewCreateInfo view{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};view.image=snapshot_;view.viewType=VK_IMAGE_VIEW_TYPE_2D;view.format=info_.format;view.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};
    if(vkCreateImageView(info_.device,&view,nullptr,&view_)!=VK_SUCCESS)return false;
    VkDescriptorImageInfo texture{VK_NULL_HANDLE,view_,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};
    VkDescriptorImageInfo sampler{sampler_,VK_NULL_HANDLE,VK_IMAGE_LAYOUT_UNDEFINED};
    VkWriteDescriptorSet writes[2]{};
    writes[0]={VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET,nullptr,set_,0,0,1,VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,&texture,nullptr,nullptr};
    writes[1]={VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET,nullptr,set_,1,0,1,VK_DESCRIPTOR_TYPE_SAMPLER,&sampler,nullptr,nullptr};
    vkUpdateDescriptorSets(info_.device,2,writes,0,nullptr);extent_=extent;return true;
}
GlassDraw VulkanGlass::prepare(const VulkanFrame& frame) {
    beginFrame();
    const auto display=ImGui::GetIO().DisplaySize;
    if(!frame.image || !(frame.imageUsage&VK_IMAGE_USAGE_TRANSFER_SRC_BIT) || !frame.extent.width || !frame.extent.height ||
        display.x!=frame.extent.width || display.y!=frame.extent.height || !initialize() || !resize(frame.extent))return {};
    source_=frame.image;
    return [this](ImDrawList& list,const GlassPanel& panel) {
        const auto clipMin=list.GetClipRectMin(),clipMax=list.GetClipRectMax();
        const float left=std::max({0.f,panel.min.x,clipMin.x}),top=std::max({0.f,panel.min.y,clipMin.y});
        const float right=std::min({float(extent_.width),panel.max.x,clipMax.x}),bottom=std::min({float(extent_.height),panel.max.y,clipMax.y});
        if(right<=left || bottom<=top)return;
        const float pad=panel.refraction+panel.blur+2;
        int l=static_cast<int>(std::max(0.f,std::floor(left-pad))),t=static_cast<int>(std::max(0.f,std::floor(top-pad)));
        int r=static_cast<int>(std::min(float(extent_.width),std::ceil(right+pad))),b=static_cast<int>(std::min(float(extent_.height),std::ceil(bottom+pad)));
        if(hasRegion_) {r=std::max(r,region_.offset.x+int(region_.extent.width));b=std::max(b,region_.offset.y+int(region_.extent.height));l=std::min(l,region_.offset.x);t=std::min(t,region_.offset.y);}
        region_={{l,t},{uint32_t(r-l),uint32_t(b-t)}};hasRegion_=true;
        struct Payload {VulkanGlass* owner;GlassPanel panel;};Payload payload{this,panel};
        list.AddCallback([](const ImDrawList*,const ImDrawCmd* cmd) {
            const auto& p=*static_cast<const Payload*>(cmd->UserCallbackData);p.owner->drawPanel(p.panel,*cmd);
        },&payload,sizeof(payload));
        list.AddCallback(ImDrawCallback_ResetRenderState,nullptr);
    };
}
GlassCaptureStats VulkanGlass::capture(VkCommandBuffer command) {
    if(!source_ || !hasRegion_)return {};
    // The submit waits at TRANSFER as well as COLOR_ATTACHMENT_OUTPUT. Without
    // that earlier wait the copy can race the application's final scene writes.
    VkImageMemoryBarrier before[]={
        barrier(source_,VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,0,VK_ACCESS_TRANSFER_READ_BIT),
        barrier(snapshot_,VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,0,VK_ACCESS_TRANSFER_WRITE_BIT)};
    vkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT,0,0,nullptr,0,nullptr,2,before);
    VkImageCopy copy{};copy.srcSubresource=copy.dstSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};
    copy.srcOffset=copy.dstOffset={region_.offset.x,region_.offset.y,0};copy.extent={region_.extent.width,region_.extent.height,1};
    vkCmdCopyImage(command,source_,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,snapshot_,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&copy);
    VkImageMemoryBarrier after[]={
        barrier(source_,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,VK_ACCESS_TRANSFER_READ_BIT,0),
        barrier(snapshot_,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,VK_ACCESS_TRANSFER_WRITE_BIT,VK_ACCESS_SHADER_READ_BIT)};
    vkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT|VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,0,0,nullptr,0,nullptr,2,after);
    source_=VK_NULL_HANDLE;hasRegion_=false;
    return {1,region_.extent.width,region_.extent.height,false};
}
void VulkanGlass::drawPanel(const GlassPanel& panel,const ImDrawCmd& draw) {
    const auto* state=static_cast<const ImGui_ImplVulkan_RenderState*>(ImGui::GetPlatformIO().Renderer_RenderState);
    if(!state)return;
    const int l=static_cast<int>(std::max({0.f,std::floor(panel.min.x-1),draw.ClipRect.x}));
    const int t=static_cast<int>(std::max({0.f,std::floor(panel.min.y-1),draw.ClipRect.y}));
    const int r=static_cast<int>(std::min({float(extent_.width),std::ceil(panel.max.x+1),draw.ClipRect.z}));
    const int b=static_cast<int>(std::min({float(extent_.height),std::ceil(panel.max.y+1),draw.ClipRect.w}));
    if(r<=l || b<=t)return;
    const auto values=panel.shaderConstants(float(extent_.width),float(extent_.height));
    vkCmdBindPipeline(state->CommandBuffer,VK_PIPELINE_BIND_POINT_GRAPHICS,pipeline_);
    vkCmdBindDescriptorSets(state->CommandBuffer,VK_PIPELINE_BIND_POINT_GRAPHICS,layout_,0,1,&set_,0,nullptr);
    vkCmdPushConstants(state->CommandBuffer,layout_,VK_SHADER_STAGE_FRAGMENT_BIT,0,sizeof(values),values.data());
    VkViewport viewport{0,0,float(extent_.width),float(extent_.height),0,1};vkCmdSetViewport(state->CommandBuffer,0,1,&viewport);
    VkRect2D scissor{{l,t},{uint32_t(r-l),uint32_t(b-t)}};vkCmdSetScissor(state->CommandBuffer,0,1,&scissor);
    vkCmdDraw(state->CommandBuffer,3,1,0,0);
    // The following ImGui reset callback rebinds its pipeline, viewport and vertex
    // push constants. Its next normal draw binds its own texture descriptor.
}
VulkanGlass::~VulkanGlass() {
    releaseSnapshot();
    if(pipeline_)vkDestroyPipeline(info_.device,pipeline_,nullptr);
    if(pool_)vkDestroyDescriptorPool(info_.device,pool_,nullptr);
    if(sampler_)vkDestroySampler(info_.device,sampler_,nullptr);
    if(layout_)vkDestroyPipelineLayout(info_.device,layout_,nullptr);
    if(setLayout_)vkDestroyDescriptorSetLayout(info_.device,setLayout_,nullptr);
}
}
