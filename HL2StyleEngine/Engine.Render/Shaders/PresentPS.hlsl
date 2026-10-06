Texture2D SourceTex : register(t0);
SamplerState SourceSamp : register(s0);
cbuffer Presentation : register(b0) { float4 Exposure; };

struct PSIn
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
};

float4 PSMain(PSIn input) : SV_Target0
{
    float3 radiance = max(0, SourceTex.Sample(SourceSamp, input.TexCoord).rgb) * Exposure.x;
    float3 mapped = radiance / (1 + radiance);
    float3 srgb = lerp(12.92 * mapped, 1.055 * pow(mapped, 1.0 / 2.4) - 0.055, step(0.0031308, mapped));
    return float4(srgb, 1);
}
