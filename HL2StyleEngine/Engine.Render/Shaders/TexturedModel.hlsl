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
    float4 DirectionCone;
    float4 Shadow;
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
Texture2D NormalTex : register(t2);
cbuffer ShadowMatrices : register(b3) { float4x4 LightMatrices[8]; };
Texture2DArray<float> ShadowMap : register(t3);
SamplerState ShadowSampler : register(s2);

float ShadowVisibility(PointLight light, float3 world, float3 normal, float3 toLight)
{
    if (light.Shadow.y < 0) return 1;
    int slice = (int)light.Shadow.y;
    if (light.Shadow.z > .5)
    {
        float3 d = world - light.PositionRange.xyz;
        float3 a = abs(d);
        slice += a.x >= a.y && a.x >= a.z ? (d.x >= 0 ? 0 : 1) : a.y >= a.z ? (d.y >= 0 ? 2 : 3) : (d.z >= 0 ? 4 : 5);
    }
    float4 projected = mul(LightMatrices[slice], float4(world, 1));
    float3 ndc = projected.xyz / projected.w;
    float2 uv = float2(ndc.x * .5 + .5, .5 - ndc.y * .5);
    if (projected.w <= 0 || ndc.z < 0 || ndc.z > 1 || any(uv < 0) || any(uv > 1)) return 1;
    float bias = max(.00025, .001 * (1 - saturate(dot(normal, toLight))));
    float visible = 0;
    [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
        visible += ndc.z - bias <= ShadowMap.SampleLevel(ShadowSampler, float3(uv + float2(x,y) / 1024.0, slice), 0) ? 1 : 0;
    return visible / 9;
}

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
    if (Material.z > .5)
    {
        float3 dp1 = ddx(input.WorldPosition), dp2 = ddy(input.WorldPosition);
        float2 duv1 = ddx(input.TexCoord), duv2 = ddy(input.TexCoord);
        float determinant = duv1.x * duv2.y - duv1.y * duv2.x;
        if (abs(determinant) > 1e-8)
        {
            float3 tangent = (dp1 * duv2.y - dp2 * duv1.y) / determinant;
            tangent = normalize(tangent - normal * dot(tangent, normal));
            float3 bitangent = normalize(cross(normal, tangent)) * (dot(cross(normal, tangent), (-dp1 * duv2.x + dp2 * duv1.x) / determinant) < 0 ? -1 : 1);
            float2 nxy = NormalTex.Sample(BaseColorSamp, input.TexCoord).rg * 2 - 1;
            normal = normalize(tangent * nxy.x + bitangent * nxy.y + normal * sqrt(saturate(1 - dot(nxy,nxy))));
        }
    }
    float3 lightDir = normalize(float3(-0.35, 0.65, -0.68));
    float3 viewDir = normalize(CameraPosition.xyz - input.WorldPosition);
    float3 halfDir = normalize(lightDir + viewDir);

    float ndotl = saturate(dot(normal, lightDir));
    float ndoth = saturate(dot(normal, halfDir));

    float3 ambient = baseColor.rgb * LightInfo.y;
    float3 diffuse = baseColor.rgb * ndotl * lerp(0.70, 0.28, metallic);

    float specPower = lerp(12.0, 96.0, 1.0 - roughness);
    float specStrength = lerp(0.10, 0.75, metallic) * (1.0 - roughness * 0.55);
    float3 specColor = lerp(float3(1.0, 1.0, 1.0), baseColor.rgb, metallic);
    float3 specular = specColor * pow(ndoth, specPower) * specStrength;

    float3 lit = ambient + (diffuse + specular) * LightInfo.z;
    for (int i = 0; i < (int)LightInfo.x; i++)
    {
        float3 offset = Lights[i].PositionRange.xyz - input.WorldPosition;
        float distanceToLight = length(offset);
        float falloff = saturate(1.0 - distanceToLight / max(0.01, Lights[i].PositionRange.w));
        float lambert = saturate(dot(normal, offset / max(0.001, distanceToLight)));
        float3 energy = Lights[i].ColorIntensity.rgb * Lights[i].ColorIntensity.w;
        float cone = 1;
        if (Lights[i].DirectionCone.w >= 0)
            cone = smoothstep(Lights[i].DirectionCone.w, Lights[i].Shadow.x, dot(-offset / max(.001, distanceToLight), Lights[i].DirectionCone.xyz));
        float visibility = ShadowVisibility(Lights[i], input.WorldPosition, normal, offset / max(.001, distanceToLight));
        lit += baseColor.rgb * energy * falloff * falloff * lambert * cone * visibility;
    }
    return float4(max(0, lit), baseColor.a);
}
