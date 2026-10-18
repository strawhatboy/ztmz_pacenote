// Shader Model 3 equivalent of dx11/glass.hlsl. Snapshot is GPU-only.
sampler sceneTexture : register(s0);
float4 bounds : register(c0);
float4 effects : register(c1); // radius, refraction, blur, capture ready
float4 tint : register(c2);
float4 surface : register(c3); // width, height, horizontal panel slant, unused
float4 vsMain(float2 position : POSITION0) : POSITION0 {
    return float4(position.x/surface.x*2-1,1-position.y/surface.y*2,0,1);
}
float distanceToPanel(float2 p) {
    float2 halfSize=(bounds.zw-bounds.xy)*0.5;
    // Inverse shear keeps the silhouette inside the capture/scissor bounds.
    p.x-=surface.z*(0.5-(p.y-bounds.y)/(bounds.w-bounds.y));
    halfSize.x-=surface.z*0.5;
    float2 q=abs(p-(bounds.xy+bounds.zw)*0.5)-(halfSize-effects.x);
    return length(max(q,0))+min(max(q.x,q.y),0)-effects.x;
}
float4 psMain(float2 position : VPOS) : COLOR0 {
    // DX9 VPOS uses integer pixel centers; the common scene uses half centers.
    float2 p=position+0.5;
    float d=distanceToPanel(p);clip(0.75-d);
    float coverage=saturate(0.5-d);
    float2 normal=float2(distanceToPanel(p+float2(0.5,0))-distanceToPanel(p-float2(0.5,0)),
                         distanceToPanel(p+float2(0,0.5))-distanceToPanel(p-float2(0,0.5)));
    normal/=max(length(normal),0.001);
    float bevel=max(4,min(effects.x,32));
    float edge=1-saturate(-d/bevel);
    float rim=(1-saturate(abs(d+0.7)/1.5))*0.22;
    float light=saturate(dot(normal,normalize(float2(-0.6,-0.8))));
    // StretchRect can fail (device loss/driver restrictions). Never sample a
    // stale image then: keep the command as a translucent tinted rounded panel.
    [branch] if(effects.w<0.5) return float4(tint.rgb+rim,coverage*max(tint.a,rim));
    float2 uv=(p-normal*effects.y*edge*edge)/surface.xy;
    float3 c=tex2Dlod(sceneTexture,float4(uv,0,0)).rgb;
    [branch] if(effects.z>0) {
        float2 blur=effects.z/surface.xy;c*=0.4;
        c+=tex2Dlod(sceneTexture,float4(uv+float2(blur.x,0),0,0)).rgb*0.15;
        c+=tex2Dlod(sceneTexture,float4(uv-float2(blur.x,0),0,0)).rgb*0.15;
        c+=tex2Dlod(sceneTexture,float4(uv+float2(0,blur.y),0,0)).rgb*0.15;
        c+=tex2Dlod(sceneTexture,float4(uv-float2(0,blur.y),0,0)).rgb*0.15;
    }
    c=lerp(c,tint.rgb,tint.a);
    c+=rim*(0.25+0.75*light)+edge*edge*light*0.025;
    return float4(c,coverage);
}
