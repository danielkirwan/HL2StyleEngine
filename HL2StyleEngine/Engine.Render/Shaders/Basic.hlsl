cbuffer Camera : register(b0)
{
    float4x4 ViewProj;
};

cbuffer Object : register(b1)
{
    float4x4 Model;
    float4 Color;
};

struct VSInput
{
    float3 Position : POSITION;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
};

VSOutput VSMain(VSInput input)
{
    VSOutput o;

    float4 worldPos = mul(Model, float4(input.Position, 1.0));
    o.Position = mul(ViewProj, worldPos);
    o.Color = Color;

    return o;
}

float4 PSMain(VSOutput input) : SV_TARGET
{
    float3 radiance = lerp(input.Color.rgb / 12.92, pow(max(0, (input.Color.rgb + .055) / 1.055), 2.4), step(.04045, input.Color.rgb));
    return float4(radiance, input.Color.a);
}
