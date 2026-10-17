#include "graphics/vulkan/backend.hpp"
#include "graphics/vulkan/glass.hpp"
#include <imgui_impl_vulkan.h>
#include <cstring>
#include <stdexcept>
#include <type_traits>
namespace ztmz {
namespace {
void check(VkResult r){if(r!=VK_SUCCESS) throw std::runtime_error("Vulkan operation failed: "+std::to_string(r));}
template<class T> ImTextureID textureId(T handle){if constexpr(std::is_pointer_v<T>) return static_cast<ImTextureID>(reinterpret_cast<std::uintptr_t>(handle));else return static_cast<ImTextureID>(handle);}
class VulkanBackend final:public RenderBackend {
    VulkanBackendInfo info_;
    VkRenderPass renderPass_=VK_NULL_HANDLE;VkDescriptorPool descriptors_=VK_NULL_HANDLE;
    VkCommandPool pool_=VK_NULL_HANDLE;VkCommandBuffer command_=VK_NULL_HANDLE;
    VkFence fence_=VK_NULL_HANDLE;VkSampler sampler_=VK_NULL_HANDLE;
    bool initialized_=false;
    bool recording_=false,submitted_=false;
    struct Staging {VkDevice d;VkBuffer b=VK_NULL_HANDLE;VkDeviceMemory m=VK_NULL_HANDLE;~Staging(){if(b) vkDestroyBuffer(d,b,nullptr);if(m) vkFreeMemory(d,m,nullptr);}};
    std::vector<std::unique_ptr<Staging>> staging_;
    struct Target {VkDevice d;VkImageView view=VK_NULL_HANDLE;VkFramebuffer framebuffer=VK_NULL_HANDLE;~Target(){if(framebuffer) vkDestroyFramebuffer(d,framebuffer,nullptr);if(view) vkDestroyImageView(d,view,nullptr);}};
    std::unique_ptr<Target> target_;
    struct Image {
        VkDevice device=VK_NULL_HANDLE;VkImage image=VK_NULL_HANDLE;VkDeviceMemory memory=VK_NULL_HANDLE;
        VkImageView view=VK_NULL_HANDLE;VkDescriptorSet descriptor=VK_NULL_HANDLE;
        std::shared_ptr<const ImageAsset> source;
        ~Image(){if(descriptor) ImGui_ImplVulkan_RemoveTexture(descriptor);if(view) vkDestroyImageView(device,view,nullptr);if(image) vkDestroyImage(device,image,nullptr);if(memory) vkFreeMemory(device,memory,nullptr);}
    };
    std::map<std::string,std::unique_ptr<Image>> images_;
    std::unique_ptr<Image> bootstrap_;
    std::unique_ptr<VulkanGlass> glass_;
    uint32_t memoryType(uint32_t bits,VkMemoryPropertyFlags properties) {
        VkPhysicalDeviceMemoryProperties memory{};vkGetPhysicalDeviceMemoryProperties(info_.physicalDevice,&memory);
        for(uint32_t i=0;i<memory.memoryTypeCount;++i) if((bits&(1u<<i)) && (memory.memoryTypes[i].propertyFlags&properties)==properties) return i;
        throw std::runtime_error("No suitable Vulkan memory type");
    }
    void begin() {
        if(recording_)return;
        // readyForFrame has established completion before any resource reuse.
        staging_.clear();target_.reset();
        check(vkResetCommandPool(info_.device,pool_,0));
        VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};begin.flags=VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        check(vkBeginCommandBuffer(command_,&begin));
        recording_=true;
    }
    void finish(VulkanFrame* frame=nullptr) {
        check(vkEndCommandBuffer(command_));check(vkResetFences(info_.device,1,&fence_));
        const VkPipelineStageFlags waitStages=VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT
            | ((frame && frame->glassCapture.copies)?VK_PIPELINE_STAGE_TRANSFER_BIT:0);
        std::vector<VkPipelineStageFlags> stages(frame?frame->waits.size():0,waitStages);
        VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO};submit.commandBufferCount=1;submit.pCommandBuffers=&command_;
        if(frame){submit.waitSemaphoreCount=static_cast<uint32_t>(frame->waits.size());submit.pWaitSemaphores=frame->waits.data();submit.pWaitDstStageMask=stages.data();}
        if(frame&&frame->signal){submit.signalSemaphoreCount=1;submit.pSignalSemaphores=&frame->signal;}
        auto result=vkQueueSubmit(info_.queue,1,&submit,fence_);
        recording_=false;submitted_=result==VK_SUCCESS;
        if(frame) {frame->completion=result;frame->waitsConsumed=result==VK_SUCCESS;}
        check(result);
    }
    std::unique_ptr<Image> upload(std::shared_ptr<const ImageAsset> source) {
        auto image=std::make_unique<Image>();image->device=info_.device;image->source=std::move(source);
        VkImageCreateInfo create{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};create.imageType=VK_IMAGE_TYPE_2D;create.format=VK_FORMAT_R8G8B8A8_UNORM;create.extent={image->source->width,image->source->height,1};create.mipLevels=create.arrayLayers=1;create.samples=VK_SAMPLE_COUNT_1_BIT;create.tiling=VK_IMAGE_TILING_OPTIMAL;create.usage=VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT;create.sharingMode=VK_SHARING_MODE_EXCLUSIVE;
        check(vkCreateImage(info_.device,&create,nullptr,&image->image));
        VkMemoryRequirements requirements{};vkGetImageMemoryRequirements(info_.device,image->image,&requirements);
        VkMemoryAllocateInfo allocation{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};allocation.allocationSize=requirements.size;allocation.memoryTypeIndex=memoryType(requirements.memoryTypeBits,VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        check(vkAllocateMemory(info_.device,&allocation,nullptr,&image->memory));check(vkBindImageMemory(info_.device,image->image,image->memory,0));
        auto pending=std::make_unique<Staging>();pending->d=info_.device;auto& staging=*pending;
        VkBufferCreateInfo buffer{VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO};buffer.size=image->source->rgba.size();buffer.usage=VK_BUFFER_USAGE_TRANSFER_SRC_BIT;buffer.sharingMode=VK_SHARING_MODE_EXCLUSIVE;
        check(vkCreateBuffer(info_.device,&buffer,nullptr,&staging.b));vkGetBufferMemoryRequirements(info_.device,staging.b,&requirements);
        allocation.allocationSize=requirements.size;allocation.memoryTypeIndex=memoryType(requirements.memoryTypeBits,VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT|VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
        check(vkAllocateMemory(info_.device,&allocation,nullptr,&staging.m));check(vkBindBufferMemory(info_.device,staging.b,staging.m,0));
        void* data=nullptr;check(vkMapMemory(info_.device,staging.m,0,buffer.size,0,&data));std::memcpy(data,image->source->rgba.data(),image->source->rgba.size());vkUnmapMemory(info_.device,staging.m);
        begin();VkImageMemoryBarrier barrier{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};barrier.srcQueueFamilyIndex=barrier.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;barrier.image=image->image;barrier.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};barrier.oldLayout=VK_IMAGE_LAYOUT_UNDEFINED;barrier.newLayout=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;barrier.dstAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT;
        vkCmdPipelineBarrier(command_,VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT,0,0,nullptr,0,nullptr,1,&barrier);
        VkBufferImageCopy copy{};copy.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};copy.imageExtent=create.extent;
        vkCmdCopyBufferToImage(command_,staging.b,image->image,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&copy);
        barrier.oldLayout=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;barrier.newLayout=VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;barrier.srcAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT;barrier.dstAccessMask=VK_ACCESS_SHADER_READ_BIT;
        vkCmdPipelineBarrier(command_,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,0,0,nullptr,0,nullptr,1,&barrier);
        staging_.push_back(std::move(pending)); // retained until this submission's fence completes
        VkImageViewCreateInfo view{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};view.image=image->image;view.viewType=VK_IMAGE_VIEW_TYPE_2D;view.format=create.format;view.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};check(vkCreateImageView(info_.device,&view,nullptr,&image->view));
        image->descriptor=ImGui_ImplVulkan_AddTexture(sampler_,image->view,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);if(!image->descriptor) throw std::runtime_error("Vulkan texture descriptor allocation failed");return image;
    }
public:
    explicit VulkanBackend(const VulkanBackendInfo& info):info_(info) {
        if(!info.device || !info.queue || !info.instance || !info.physicalDevice || info.imageCount<2) throw std::invalid_argument("Incomplete Vulkan device/queue/swap-chain context");
    }
    RenderApi api() const noexcept override{return RenderApi::Vulkan;}
    void initialize() override {
        VkAttachmentDescription attachment{};attachment.format=info_.format;attachment.samples=VK_SAMPLE_COUNT_1_BIT;attachment.loadOp=VK_ATTACHMENT_LOAD_OP_LOAD;attachment.storeOp=VK_ATTACHMENT_STORE_OP_STORE;attachment.stencilLoadOp=VK_ATTACHMENT_LOAD_OP_DONT_CARE;attachment.stencilStoreOp=VK_ATTACHMENT_STORE_OP_DONT_CARE;attachment.initialLayout=attachment.finalLayout=VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
        VkAttachmentReference color{0,VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL};VkSubpassDescription subpass{};subpass.pipelineBindPoint=VK_PIPELINE_BIND_POINT_GRAPHICS;subpass.colorAttachmentCount=1;subpass.pColorAttachments=&color;
        VkSubpassDependency dependency{};dependency.srcSubpass=VK_SUBPASS_EXTERNAL;dependency.dstSubpass=0;dependency.srcStageMask=dependency.dstStageMask=VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;dependency.dstAccessMask=VK_ACCESS_COLOR_ATTACHMENT_READ_BIT|VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        VkRenderPassCreateInfo render{VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO};render.attachmentCount=1;render.pAttachments=&attachment;render.subpassCount=1;render.pSubpasses=&subpass;render.dependencyCount=1;render.pDependencies=&dependency;check(vkCreateRenderPass(info_.device,&render,nullptr,&renderPass_));
        VkDescriptorPoolSize size{VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,4096};VkDescriptorPoolCreateInfo descriptors{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};descriptors.flags=VK_DESCRIPTOR_POOL_CREATE_FREE_DESCRIPTOR_SET_BIT;descriptors.maxSets=4096;descriptors.poolSizeCount=1;descriptors.pPoolSizes=&size;check(vkCreateDescriptorPool(info_.device,&descriptors,nullptr,&descriptors_));
        VkCommandPoolCreateInfo pool{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};pool.queueFamilyIndex=info_.queueFamily;pool.flags=VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;check(vkCreateCommandPool(info_.device,&pool,nullptr,&pool_));
        VkCommandBufferAllocateInfo commands{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};commands.commandPool=pool_;commands.level=VK_COMMAND_BUFFER_LEVEL_PRIMARY;commands.commandBufferCount=1;check(vkAllocateCommandBuffers(info_.device,&commands,&command_));
        VkFenceCreateInfo fence{VK_STRUCTURE_TYPE_FENCE_CREATE_INFO};check(vkCreateFence(info_.device,&fence,nullptr,&fence_));
        VkSamplerCreateInfo sampler{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO};sampler.magFilter=sampler.minFilter=VK_FILTER_LINEAR;sampler.mipmapMode=VK_SAMPLER_MIPMAP_MODE_LINEAR;sampler.addressModeU=sampler.addressModeV=sampler.addressModeW=VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;sampler.maxLod=1;check(vkCreateSampler(info_.device,&sampler,nullptr,&sampler_));
        ImGui_ImplVulkan_InitInfo init{};init.ApiVersion=VK_API_VERSION_1_0;init.Instance=info_.instance;init.PhysicalDevice=info_.physicalDevice;init.Device=info_.device;init.QueueFamily=info_.queueFamily;init.Queue=info_.queue;init.DescriptorPool=descriptors_;init.RenderPass=renderPass_;init.MinImageCount=info_.minImageCount;init.ImageCount=info_.imageCount;init.MSAASamples=VK_SAMPLE_COUNT_1_BIT;init.CheckVkResultFn=check;
        if(!ImGui_ImplVulkan_Init(&init)) throw std::runtime_error("ImGui Vulkan initialization failed");initialized_=true;
    }
    bool readyForFrame() const override {
        if(!submitted_)return true;
        auto result=vkGetFenceStatus(info_.device,fence_);
        if(result==VK_NOT_READY)return false;
        check(result);return true;
    }
    void newFrame() override {
        begin();
        if(glass_)glass_->beginFrame();
        if(!bootstrap_) {
            unsigned char* pixels=nullptr;int width=0,height=0;
            auto& atlas=*ImGui::GetIO().Fonts;atlas.GetTexDataAsRGBA32(&pixels,&width,&height);
            auto image=std::make_shared<ImageAsset>();image->width=width;image->height=height;
            image->rgba.assign(pixels,pixels+static_cast<size_t>(width)*height*4);
            bootstrap_=upload(image);atlas.SetTexID(textureId(bootstrap_->descriptor));
        }
        // Do not call ImGui_ImplVulkan_NewFrame: its font upload submits and waits
        // on the game queue. Our bootstrap texture joins the normal async batch.
    }
    void syncImages(const ImageAssets& images) override {
        begin(); // previous fence is complete; texture retirement is now safe
        for(auto i=images_.begin();i!=images_.end();) {auto next=images.find(i->first);if(next==images.end() || next->second!=i->second->source) i=images_.erase(i);else ++i;}
        for(const auto& [key,image]:images) if(!images_.contains(key)){images_.emplace(key,upload(image));break;}
    }
    Texture texture(const std::string& key) const override{auto i=images_.find(key);if(i==images_.end()) return {};return {textureId(i->second->descriptor),i->second->source->width,i->second->source->height};}
    GlassDraw prepareGlass(NativeFrame& native) override {
        auto* frame=dynamic_cast<VulkanFrame*>(&native);if(!frame)return {};
        if(!glass_)glass_=std::make_unique<VulkanGlass>(info_,renderPass_);
        return glass_->prepare(*frame);
    }
    void submit(ImDrawData& data,NativeFrame& native) override {
        auto* frame=dynamic_cast<VulkanFrame*>(&native);if(!frame || !frame->image) throw std::invalid_argument("Vulkan present image missing");
        if(!frame->signal)throw std::invalid_argument("Vulkan presentation semaphore missing");
        begin();target_=std::make_unique<Target>();target_->d=info_.device;auto& target=*target_;
        VkImageViewCreateInfo view{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};view.image=frame->image;view.viewType=VK_IMAGE_VIEW_TYPE_2D;view.format=info_.format;view.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};check(vkCreateImageView(info_.device,&view,nullptr,&target.view));
        VkFramebufferCreateInfo framebuffer{VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO};framebuffer.renderPass=renderPass_;framebuffer.attachmentCount=1;framebuffer.pAttachments=&target.view;framebuffer.width=frame->extent.width;framebuffer.height=frame->extent.height;framebuffer.layers=1;check(vkCreateFramebuffer(info_.device,&framebuffer,nullptr,&target.framebuffer));
        begin();frame->glassCapture=glass_?glass_->capture(command_):GlassCaptureStats{};
        VkRenderPassBeginInfo beginPass{VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO};beginPass.renderPass=renderPass_;beginPass.framebuffer=target.framebuffer;beginPass.renderArea.extent=frame->extent;
        vkCmdBeginRenderPass(command_,&beginPass,VK_SUBPASS_CONTENTS_INLINE);ImGui_ImplVulkan_RenderDrawData(&data,command_);vkCmdEndRenderPass(command_);finish(frame);
    }
    void invalidateDeviceObjects() override {
        if(submitted_)check(vkWaitForFences(info_.device,1,&fence_,VK_TRUE,UINT64_MAX));
        glass_.reset();target_.reset();staging_.clear();images_.clear();bootstrap_.reset();ImGui::GetIO().Fonts->SetTexID(0);
    }
    void shutdown() noexcept override {
        if(submitted_&&fence_)vkWaitForFences(info_.device,1,&fence_,VK_TRUE,UINT64_MAX);
        glass_.reset();staging_.clear();target_.reset();images_.clear();bootstrap_.reset();if(initialized_ || ImGui::GetIO().BackendRendererUserData) ImGui_ImplVulkan_Shutdown();initialized_=false;
        if(sampler_) vkDestroySampler(info_.device,sampler_,nullptr);if(fence_) vkDestroyFence(info_.device,fence_,nullptr);if(pool_) vkDestroyCommandPool(info_.device,pool_,nullptr);if(descriptors_) vkDestroyDescriptorPool(info_.device,descriptors_,nullptr);if(renderPass_) vkDestroyRenderPass(info_.device,renderPass_,nullptr);
        sampler_=VK_NULL_HANDLE;fence_=VK_NULL_HANDLE;pool_=VK_NULL_HANDLE;descriptors_=VK_NULL_HANDLE;renderPass_=VK_NULL_HANDLE;
    }
};
}
std::unique_ptr<RenderBackend> createVulkanBackend(const VulkanBackendInfo& info){return std::make_unique<VulkanBackend>(info);}
}
