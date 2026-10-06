cbuffer ShadowCamera : register(b0) { float4x4 LightViewProj; };
cbuffer ShadowObject : register(b1) { float4x4 Model; };
float4 VSMain(float3 position : POSITION) : SV_POSITION
{
    return mul(LightViewProj, mul(Model, float4(position, 1)));
}
