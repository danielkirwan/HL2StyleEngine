struct VSIn
{
    float2 Position : POSITION;
    float2 TexCoord : TEXCOORD0;
};

struct VSOut
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
};

VSOut VSMain(VSIn input)
{
    VSOut o;
    o.Position = float4(input.Position, 0, 1);

    float2 uv = input.TexCoord;

    //uv.y = 1.0f - uv.y;

    o.TexCoord = uv;
    return o;
}
