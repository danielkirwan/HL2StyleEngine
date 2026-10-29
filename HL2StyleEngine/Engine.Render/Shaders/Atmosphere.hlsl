Texture2D SourceTex : register(t0);
Texture2D BloomTex : register(t1);
Texture2DMS<float,4> SceneDepth : register(t2);
Texture2DMS<float4,4> SceneColor : register(t3);
SamplerState LinearClamp : register(s0);
cbuffer Atmosphere : register(b0)
{
    float4x4 InverseViewProjection;
    float4 Camera;
    float4 SceneViewport;
    float4 Dimensions;
    float4 Bloom;
    float4 Grade;
    float4 ColorFilter;
    float4 FogColor;
    float4 Fog;
};
struct PSIn { float4 Position : SV_Position; float2 TexCoord : TEXCOORD0; };
bool InScene(float2 uv)
{
    float2 pixel=uv*Dimensions.xy;
    return all(pixel>=SceneViewport.xy) && all(pixel<SceneViewport.xy+SceneViewport.zw);
}
float3 FogSample(float3 radiance,float depth,float2 uv)
{
    float2 local=(uv*Dimensions.xy-SceneViewport.xy)/SceneViewport.zw;
    float4 position=mul(InverseViewProjection,float4(local.x*2-1,1-local.y*2,depth,1));
    float3 world=position.xyz/max(abs(position.w),1e-7)*sign(position.w);
    float3 ray=world-Camera.xyz;
    float distance=length(ray), start=min(Fog.y,distance);
    float lengthInFog=max(0,distance-start);
    float startHeight=lerp(Camera.y,world.y,start/max(distance,1e-6));
    // Integrate exponential height density along the ray; a zero falloff gives distance fog.
    float h0=clamp(-Fog.w*(startHeight-Fog.z),-20,20);
    float h1=clamp(-Fog.w*(world.y-Fog.z),-20,20);
    float delta=h1-h0;
    float average=abs(delta)<.001 ? exp((h0+h1)*.5) : (exp(h1)-exp(h0))/delta;
    float opacity=min(FogColor.w,1-exp(-min(80,Fog.x*lengthInFog*average)));
    return lerp(radiance,FogColor.rgb,opacity);
}
float3 Scene(float2 uv)
{
    if(!InScene(uv))return 0;
    if(Fog.x<=0)return max(0,SourceTex.SampleLevel(LinearClamp,uv,0).rgb);
    int2 p=clamp(int2(uv*Dimensions.xy),int2(0,0),int2(Dimensions.xy)-1);
    float3 sum=0;
    // Fog each MSAA sample before resolving, keeping foreground/background edges separate.
    [unroll] for(int sample=0;sample<4;sample++)
        sum+=FogSample(max(0,SceneColor.Load(p,sample).rgb),SceneDepth.Load(p,sample),(p+.5)/Dimensions.xy);
    return sum*.25;
}
float3 Threshold(float3 color)
{
    color=min(color,64);
    float brightness=max(color.r,max(color.g,color.b)),knee=Bloom.y*.5;
    float soft=clamp(brightness-Bloom.y+knee,0,2*knee);
    soft=soft*soft/(4*knee+1e-6);
    return color*(max(brightness-Bloom.y,soft)/max(brightness,1e-6));
}
float4 ExtractPS(PSIn input):SV_Target0
{
    float2 offset=.25/Dimensions.zw;
    float3 c=Threshold(Scene(input.TexCoord+float2(-offset.x,-offset.y)));
    c+=Threshold(Scene(input.TexCoord+float2(offset.x,-offset.y)));
    c+=Threshold(Scene(input.TexCoord+float2(-offset.x,offset.y)));
    c+=Threshold(Scene(input.TexCoord+offset));
    return float4(c*.25,1);
}
float4 Blur(float2 uv,float2 stepSize)
{
    float3 c=BloomTex.SampleLevel(LinearClamp,uv,0).rgb*.227027;
    c+=BloomTex.SampleLevel(LinearClamp,uv+stepSize*1.384615,0).rgb*.316216;
    c+=BloomTex.SampleLevel(LinearClamp,uv-stepSize*1.384615,0).rgb*.316216;
    c+=BloomTex.SampleLevel(LinearClamp,uv+stepSize*3.230769,0).rgb*.070270;
    c+=BloomTex.SampleLevel(LinearClamp,uv-stepSize*3.230769,0).rgb*.070270;
    return float4(c,1);
}
float4 BlurHorizontalPS(PSIn input):SV_Target0{return Blur(input.TexCoord,float2(1/Dimensions.z,0));}
float4 BlurVerticalPS(PSIn input):SV_Target0{return Blur(input.TexCoord,float2(0,1/Dimensions.w));}
float4 CompositePS(PSIn input):SV_Target0
{
    if(!InScene(input.TexCoord))return float4(0,0,0,1);
    float3 radiance=Scene(input.TexCoord)+Bloom.x*BloomTex.SampleLevel(LinearClamp,input.TexCoord,0).rgb;
    radiance*=Grade.x;
    if(Grade.w>0)radiance*=ColorFilter.rgb;
    float3 mapped=radiance/(1+radiance);
    if(Grade.w>0)
    {
        float luminance=dot(mapped,float3(.2126,.7152,.0722));
        mapped=lerp(luminance.xxx,mapped,Grade.z);
        mapped=saturate((mapped-.18)*Grade.y+.18);
    }
    float3 srgb=lerp(12.92*mapped,1.055*pow(max(0,mapped),1.0/2.4)-.055,step(.0031308,mapped));
    return float4(srgb,1);
}
