// Vulkan/SPIR-V counterpart of the DX11 convex-edge glass shader.
// The snapshot is captured before HUD drawing; foreground instruments stay sharp.
[[vk::binding(0,0)]] Texture2D sceneTexture;
[[vk::binding(1,0)]] SamplerState sceneSampler;
// surface: width, height, horizontal panel slant, unused.
struct Panel { float4 bounds; float4 effects; float4 tint; float4 surface; };
[[vk::push_constant]] ConstantBuffer<Panel> panel;

float4 vsMain(uint id : SV_VertexID) : SV_POSITION {
    // Vulkan's positive viewport maps NDC -Y to the top. No Y-flip is required
    // for this full-screen triangle; sampling uses framebuffer SV_POSITION.
    return float4(id==2?3:-1,id==1?3:-1,0,1);
}
float distanceToPanel(float2 p) {
    float2 halfSize=(panel.bounds.zw-panel.bounds.xy)*0.5;
    // Inverse shear keeps the silhouette inside the capture/scissor bounds.
    p.x-=panel.surface.z*(0.5-(p.y-panel.bounds.y)/(panel.bounds.w-panel.bounds.y));
    halfSize.x-=panel.surface.z*0.5;
    float2 q=abs(p-(panel.bounds.xy+panel.bounds.zw)*0.5)-(halfSize-panel.effects.x);
    return length(max(q,0))+min(max(q.x,q.y),0)-panel.effects.x;
}
float4 psMain(float4 position : SV_POSITION) : SV_TARGET {
    float2 p=position.xy;
    float d=distanceToPanel(p);
    clip(0.75-d);
    float coverage=saturate(0.5-d);
    float2 normal=float2(distanceToPanel(p+float2(0.5,0))-distanceToPanel(p-float2(0.5,0)),
                         distanceToPanel(p+float2(0,0.5))-distanceToPanel(p-float2(0,0.5)));
    normal/=max(length(normal),0.001);
    float bevel=max(4,min(panel.effects.x,32));
    float edge=1-saturate(-d/bevel);
    float2 uv=(p-normal*panel.effects.y*edge*edge)/panel.surface.xy;
    float3 c=sceneTexture.Sample(sceneSampler,uv).rgb;
    [branch] if(panel.effects.z>0) {
        float2 blur=panel.effects.z/panel.surface.xy;
        c*=0.4;
        c+=sceneTexture.Sample(sceneSampler,uv+float2(blur.x,0)).rgb*0.15;
        c+=sceneTexture.Sample(sceneSampler,uv-float2(blur.x,0)).rgb*0.15;
        c+=sceneTexture.Sample(sceneSampler,uv+float2(0,blur.y)).rgb*0.15;
        c+=sceneTexture.Sample(sceneSampler,uv-float2(0,blur.y)).rgb*0.15;
    }
    c=lerp(c,panel.tint.rgb,panel.tint.a);
    float rim=(1-saturate(abs(d+0.7)/1.5))*0.22;
    float light=saturate(dot(normal,normalize(float2(-0.6,-0.8))));
    c+=rim*(0.25+0.75*light)+edge*edge*light*0.025;
    return float4(c,coverage);
}
