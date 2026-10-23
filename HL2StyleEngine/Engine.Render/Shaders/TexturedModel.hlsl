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
    float4 Emission;
    float4 ProbeA;
    float4 ProbeB;
    float4 ProbeC;
    float4 ProbeD;
    float4 ProbeE;
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
    float4 ShadingSettings; // x: explicit lighting model; y: diagnostic. Object/lightmap fields remain unchanged.
};

Texture2D BaseColorTex : register(t0);
SamplerState BaseColorSamp : register(s0);
Texture2D MetallicRoughnessTex : register(t1);
SamplerState MetallicRoughnessSamp : register(s1);
Texture2D NormalTex : register(t2);
Texture2D EmissiveTex : register(t3);
cbuffer ShadowMatrices : register(b3) { float4x4 LightMatrices[8]; };
Texture2DArray<float> ShadowMap : register(t4);
SamplerState ShadowSampler : register(s2);
Texture2D LightmapTex : register(t5);
SamplerState LightmapSampler : register(s3);
TextureCube<float4> EnvironmentTex : register(t6);
Texture2D<float2> EnvironmentBrdf : register(t7);
SamplerState EnvironmentSampler : register(s4);
cbuffer ReflectionSettings : register(b4) { float4 EnvironmentInfo; };

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
    float2 EmissiveTexCoord : TEXCOORD1;
    float2 LightmapTexCoord : TEXCOORD2;
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

    float4 worldPos = mul(Model, float4(input.Position, 1.0));
    o.Position = mul(ViewProj, worldPos);
    o.WorldPosition = worldPos.xyz;
    o.Normal = normalize(mul((float3x3)NormalMatrix, input.Normal));
    o.TexCoord = input.TexCoord;
    o.EmissiveTexCoord = input.EmissiveTexCoord;
    o.LightmapTexCoord = input.LightmapTexCoord;
    o.Color = Color;

    return o;
}

float3 SafeNormal(float3 v, float3 fallback)
{
    float lengthSquared = dot(v, v);
    return isfinite(lengthSquared) && lengthSquared > 1e-12 ? v * rsqrt(lengthSquared) : fallback;
}

float3 PbrNormal(VSOutput input)
{
    float3 n = SafeNormal(input.Normal, float3(0,1,0));
    if (Material.z <= .5) return n;
    float3 dp1 = ddx(input.WorldPosition), dp2 = ddy(input.WorldPosition);
    float2 duv1 = ddx(input.TexCoord), duv2 = ddy(input.TexCoord);
    float determinant = duv1.x * duv2.y - duv1.y * duv2.x;
    if (abs(determinant) <= 1e-8) return n;
    float3 t = (dp1 * duv2.y - dp2 * duv1.y) / determinant;
    t -= n * dot(t, n);
    if (!all(isfinite(t)) || dot(t,t) <= 1e-12) return n;
    t = normalize(t);
    float3 b = cross(n,t);
    b *= dot(b, (-dp1 * duv2.x + dp2 * duv1.x) / determinant) < 0 ? -1 : 1;
    float2 xy = NormalTex.Sample(BaseColorSamp, input.TexCoord).rg * 2 - 1;
    return SafeNormal(t*xy.x + b*xy.y + n*sqrt(saturate(1-dot(xy,xy))), n);
}

// Perceptual roughness -> alpha once. Height-correlated Smith visibility includes 1/(4 NoL NoV).
float3 DirectPbr(float3 base, float metal, float roughness, float3 n, float3 v, float3 l)
{
    float nl = saturate(dot(n,l)), nv = saturate(dot(n,v));
    if (nl <= 0 || nv <= 0) return 0;
    float3 h = SafeNormal(v+l,n);
    float nh = saturate(dot(n,h)), vh = saturate(dot(v,h));
    float alpha = roughness*roughness, a2 = alpha*alpha;
    // This algebra avoids catastrophic cancellation at the smooth end of the ramp.
    float d = (1-nh*nh) + nh*nh*a2;
    float distribution = a2 / (3.14159265359*d*d);
    float visibility = .5 / max(1e-12, nl*sqrt(nv*nv*(1-a2)+a2) + nv*sqrt(nl*nl*(1-a2)+a2));
    float3 f0 = lerp(float3(.04,.04,.04),base,metal);
    float m = 1-vh, m2=m*m;
    float3 fresnel = f0 + (1-f0)*m2*m2*m;
    return ((1-fresnel)*(1-metal)*base/3.14159265359 + distribution*visibility*fresnel)*nl;
}

float3 DiffuseIrradiance(VSOutput input, float3 n)
{
    // Existing atlases/probes store diffuse response E/pi, not raw irradiance.
    if (Material.w > 1.5)
    {
        float3 x = n.x >= 0 ? ProbeA.xyz : float3(ProbeA.w,ProbeB.xy);
        float3 y = n.y >= 0 ? float3(ProbeB.zw,ProbeC.x) : ProbeC.yzw;
        float3 z = n.z >= 0 ? ProbeD.xyz : float3(ProbeD.w,ProbeE.xy);
        float3 weights = n*n;
        return weights.x*x + weights.y*y + weights.z*z;
    }
    if (Material.w > .5) return LightmapTex.SampleLevel(LightmapSampler,input.LightmapTexCoord,0).rgb;
    return 0;
}

float4 ShadePbr(VSOutput input, float4 base, float metal, float roughness)
{
    float3 n = PbrNormal(input), v = SafeNormal(CameraPosition.xyz-input.WorldPosition,n);
    int diagnostic = (int)ShadingSettings.y;
    bool full = diagnostic == 0, direct = diagnostic == 1;
    float3 lit = 0;
    if ((full || direct) && LightInfo.w < .5)
    {
        lit = DirectPbr(base.rgb,metal,roughness,n,v,normalize(float3(-.35,.65,-.68)))*LightInfo.z;
        for (int i=0; i<(int)LightInfo.x; i++)
        {
            float3 offset = Lights[i].PositionRange.xyz-input.WorldPosition;
            float distanceToLight = length(offset);
            float falloff = saturate(1-distanceToLight/max(.01,Lights[i].PositionRange.w));
            if (falloff <= 0) continue;
            float3 l = offset/max(.001,distanceToLight);
            float cone = 1;
            if (Lights[i].DirectionCone.w >= 0)
                cone = smoothstep(Lights[i].DirectionCone.w,Lights[i].Shadow.x,dot(-l,Lights[i].DirectionCone.xyz));
            if (cone <= 0 || dot(n,l) <= 0) continue;
            float visibility = ShadowVisibility(Lights[i],input.WorldPosition,n,l);
            lit += DirectPbr(base.rgb,metal,roughness,n,v,l)*Lights[i].ColorIntensity.rgb*Lights[i].ColorIntensity.w*falloff*falloff*cone*visibility;
        }
        // Authored fallback only. Do not add a second ambient term over an atlas/probe.
        if (full && Material.w < .5) lit += base.rgb*(1-metal)*LightInfo.y;
    }
    if (full || diagnostic == 2) lit += base.rgb*(1-metal)*DiffuseIrradiance(input,n);
    if ((full || diagnostic == 4) && LightInfo.w < .5 && any(Emission.rgb > 0))
        lit += EmissiveTex.Sample(BaseColorSamp,input.EmissiveTexCoord).rgb*Emission.rgb;
    // Use the geometric hemisphere: detailed normal maps can cross the view tangent at grazing angles.
    [branch] if ((full || diagnostic == 3) && LightInfo.w < .5 && EnvironmentInfo.z > .5 && dot(input.Normal,v) > 0)
    {
        float nv = saturate(dot(n,v));
        float3 reflected = reflect(-v,n);
        float3 radiance = EnvironmentTex.SampleLevel(EnvironmentSampler,reflected,roughness*EnvironmentInfo.x).rgb;
        // LUT texel centres represent (x+.5)/size; clamp sampler handles both endpoints.
        float2 brdf = EnvironmentBrdf.SampleLevel(EnvironmentSampler,float2(nv,roughness),0);
        float3 f0 = lerp(float3(.04,.04,.04),base.rgb,metal);
        lit += radiance*(f0*brdf.x+brdf.y);
    }
    // Preserve finite storage even for a smooth metal under very intense authored lights.
    return float4(min(65504,max(0,lit)),base.a);
}

float4 PSMain(VSOutput input) : SV_TARGET
{
    float4 baseColor = BaseColorTex.Sample(BaseColorSamp, input.TexCoord) * input.Color;
    float4 metalRough = MetallicRoughnessTex.Sample(MetallicRoughnessSamp, input.TexCoord);

    float metallic = saturate(metalRough.b * Material.x);
    float roughness = saturate(max(0.04, metalRough.g * Material.y));

    [branch] if (ShadingSettings.x > .5) return ShadePbr(input, baseColor, metallic, roughness);

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
    float3 lit = 0;
    [branch] if (LightInfo.w < .5)
    {
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

    lit = ambient + (diffuse + specular) * LightInfo.z;
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
    }
    [branch] if (Material.w > 1.5)
    {
        float3 xLight = normal.x >= 0 ? ProbeA.xyz : float3(ProbeA.w, ProbeB.xy);
        float3 yLight = normal.y >= 0 ? float3(ProbeB.zw, ProbeC.x) : ProbeC.yzw;
        float3 zLight = normal.z >= 0 ? ProbeD.xyz : float3(ProbeD.w, ProbeE.xy);
        float3 weights = normal * normal;
        lit += baseColor.rgb * (1 - metallic) * (weights.x*xLight + weights.y*yLight + weights.z*zLight);
    }
    else if (Material.w > .5)
        lit += baseColor.rgb * (1 - metallic) * LightmapTex.SampleLevel(LightmapSampler, input.LightmapTexCoord, 0).rgb;
    // sRGB texture sampling yields linear emission; add before exposure/tone mapping.
    [branch] if (LightInfo.w < .5 && any(Emission.rgb > 0))
        lit += EmissiveTex.Sample(BaseColorSamp, input.EmissiveTexCoord).rgb * Emission.rgb;
    return float4(max(0, lit), baseColor.a);
}
