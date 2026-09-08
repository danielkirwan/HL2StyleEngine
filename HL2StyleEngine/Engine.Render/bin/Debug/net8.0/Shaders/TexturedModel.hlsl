cbuffer Camera : register(b0)
{
    float4x4 ViewProj;
    float4 CameraPosition;
};

cbuffer Object : register(b1)
{
    float4x4 Model;
    float4 Color;
    float4 Material;
    float4x4 NormalMatrix;
};

struct PointLight
{
    float4 PositionRange;
    float4 ColorIntensity;
};

cbuffer Lighting : register(b2)
{
    float4 LightInfo;
    PointLight Lights[32];
};

Texture2D BaseColorTex : register(t0);
SamplerState BaseColorSamp : register(s0);
Texture2D MetallicRoughnessTex : register(t1);
SamplerState MetallicRoughnessSamp : register(s1);

struct VSInput
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD1;
    float3 Normal : NORMAL;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
};

VSOutput VSMain(VSInput input)
{
    VSOutput o;

    float4 worldPos = mul(Model, float4(input.Position, 1.0));
    o.Position = mul(ViewProj, worldPos);
    o.WorldPosition = worldPos.xyz;
    o.Normal = normalize(mul((float3x3)NormalMatrix, input.Normal));
    o.TexCoord = input.TexCoord;
    o.Color = Color;

    return o;
}

float4 PSMain(VSOutput input) : SV_TARGET
{
    float4 baseColor = BaseColorTex.Sample(BaseColorSamp, input.TexCoord) * input.Color;
    float4 metalRough = MetallicRoughnessTex.Sample(MetallicRoughnessSamp, input.TexCoord);

    float metallic = saturate(metalRough.b * Material.x);
    float roughness = saturate(max(0.04, metalRough.g * Material.y));

    float3 normal = normalize(input.Normal);
    float3 lightDir = normalize(float3(-0.35, 0.65, -0.68));
    float3 viewDir = normalize(CameraPosition.xyz - input.WorldPosition);
    float3 halfDir = normalize(lightDir + viewDir);

    float ndotl = saturate(dot(normal, lightDir));
    float ndoth = saturate(dot(normal, halfDir));

    float3 ambient = baseColor.rgb * 0.22;
    float3 diffuse = baseColor.rgb * ndotl * lerp(0.70, 0.28, metallic);

    float specPower = lerp(12.0, 96.0, 1.0 - roughness);
    float specStrength = lerp(0.10, 0.75, metallic) * (1.0 - roughness * 0.55);
    float3 specColor = lerp(float3(1.0, 1.0, 1.0), baseColor.rgb, metallic);
    float3 specular = specColor * pow(ndoth, specPower) * specStrength;

    float3 lit = ambient + diffuse + specular;
    for (int i = 0; i < (int)LightInfo.x; i++)
    {
        float3 offset = Lights[i].PositionRange.xyz - input.WorldPosition;
        float distanceToLight = length(offset);
        float falloff = saturate(1.0 - distanceToLight / max(0.01, Lights[i].PositionRange.w));
        float lambert = saturate(dot(normal, offset / max(0.001, distanceToLight)));
        float3 energy = Lights[i].ColorIntensity.rgb * Lights[i].ColorIntensity.w;
        lit += baseColor.rgb * energy * falloff * falloff * (0.15 + 0.85 * lambert);
    }
    return float4(saturate(lit), baseColor.a);
}
