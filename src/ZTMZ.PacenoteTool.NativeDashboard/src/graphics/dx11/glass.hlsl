// Snapshot is copied before the HUD; foreground instruments never enter this texture.
Texture2D sceneTexture : register(t0);
SamplerState sceneSampler : register(s0);
cbuffer Panel : register(b0) {
    float4 bounds;
    float4 effects; // radius, refraction pixels, blur pixels, unused
    float4 tint;
    float4 surface; // width, height, horizontal panel slant, unused
};
float4 vsMain(uint id : SV_VertexID) : SV_POSITION {
    return float4(id==2?3:-1,id==1?-3:1,0,1);
}
float distanceToPanel(float2 p) {
    float2 halfSize=(bounds.zw-bounds.xy)*0.5;
    // Inverse shear keeps the silhouette inside the capture/scissor bounds.
    p.x-=surface.z*(0.5-(p.y-bounds.y)/(bounds.w-bounds.y));
    halfSize.x-=surface.z*0.5;
    float2 q=abs(p-(bounds.xy+bounds.zw)*0.5)-(halfSize-effects.x);
    return length(max(q,0))+min(max(q.x,q.y),0)-effects.x;
}
float4 psMain(float4 position : SV_POSITION) : SV_TARGET {
    float2 p=position.xy;
    float d=distanceToPanel(p);
    clip(0.75-d);
    float coverage=saturate(0.5-d);
    float2 normal=float2(distanceToPanel(p+float2(0.5,0))-distanceToPanel(p-float2(0.5,0)),
                         distanceToPanel(p+float2(0,0.5))-distanceToPanel(p-float2(0,0.5)));
    normal/=max(length(normal),0.001);
    float bevel=max(4,min(effects.x,32));
    float edge=1-saturate(-d/bevel);
    // Convex lens: strong rounded-edge refraction, quiet interior for legibility.
    float2 uv=(p-normal*effects.y*edge*edge)/surface.xy;
    float3 c=sceneTexture.Sample(sceneSampler,uv).rgb;
    // Uniform per panel: zero blur costs one sample, not five identical reads.
    [branch] if(effects.z>0) {
        float2 blur=effects.z/surface.xy;
        c*=0.4;
        c+=sceneTexture.Sample(sceneSampler,uv+float2(blur.x,0)).rgb*0.15;
        c+=sceneTexture.Sample(sceneSampler,uv-float2(blur.x,0)).rgb*0.15;
        c+=sceneTexture.Sample(sceneSampler,uv+float2(0,blur.y)).rgb*0.15;
        c+=sceneTexture.Sample(sceneSampler,uv-float2(0,blur.y)).rgb*0.15;
    }
    c=lerp(c,tint.rgb,tint.a);
    float rim=(1-saturate(abs(d+0.7)/1.5))*0.22;
    float light=saturate(dot(normal,normalize(float2(-0.6,-0.8))));
    c+=rim*(0.25+0.75*light)+edge*edge*light*0.025;
    return float4(c,coverage);
}
