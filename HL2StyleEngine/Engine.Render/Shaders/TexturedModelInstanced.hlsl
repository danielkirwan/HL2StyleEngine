// Separate vertex variant. The accepted pixel shaders and their lighting math are unchanged.
cbuffer Camera : register(b0)
{
    float4x4 ViewProj;
    float4 CameraPosition;
};
struct VSInput
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
    float2 TexCoord : TEXCOORD0;
    float2 EmissiveTexCoord : TEXCOORD1;
    float2 LightmapTexCoord : TEXCOORD2;
    float4 Model0 : TEXCOORD3;
    float4 Model1 : TEXCOORD4;
    float4 Model2 : TEXCOORD5;
    float4 Model3 : TEXCOORD6;
    float4 Normal0 : TEXCOORD7;
    float4 Normal1 : TEXCOORD8;
    float4 Normal2 : TEXCOORD9;
    float4 Normal3 : TEXCOORD10;
    float4 Color : COLOR0;
};
struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD1;
    float3 Normal : NORMAL;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
    float2 EmissiveTexCoord : TEXCOORD2;
    float2 LightmapTexCoord : TEXCOORD3;
};
VSOutput VSMain(VSInput input)
{
    VSOutput o;
    float4x4 model = transpose(float4x4(input.Model0, input.Model1, input.Model2, input.Model3));
    float4x4 normal = transpose(float4x4(input.Normal0, input.Normal1, input.Normal2, input.Normal3));
    float4 worldPos = mul(model, float4(input.Position, 1.0));
    o.Position = mul(ViewProj, worldPos);
    o.WorldPosition = worldPos.xyz;
    o.Normal = normalize(mul((float3x3)normal, input.Normal));
    o.TexCoord = input.TexCoord;
    o.EmissiveTexCoord = input.EmissiveTexCoord;
    o.LightmapTexCoord = input.LightmapTexCoord;
    o.Color = input.Color;
    return o;
}
