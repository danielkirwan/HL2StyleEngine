cbuffer CameraBuffer : register(b0)
{
    row_major float4x4 ViewProj;
};

struct VSInput
{
    float3 Position : POSITION;
    float4 Color : COLOR0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
};

VSOutput VSMain(VSInput input)
{
    VSOutput o;
    o.Position = mul(float4(input.Position, 1.0f), ViewProj);
    o.Color = input.Color;
    return o;
}

float4 PSMain(VSOutput input) : SV_Target0
{
    return input.Color;
}