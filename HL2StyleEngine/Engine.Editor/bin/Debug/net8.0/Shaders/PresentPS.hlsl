Texture2D SourceTex : register(t0);
SamplerState SourceSamp : register(s0);

struct PSIn
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD0;
};

float4 PSMain(PSIn input) : SV_Target0
{
    return SourceTex.Sample(SourceSamp, input.UV);
}
