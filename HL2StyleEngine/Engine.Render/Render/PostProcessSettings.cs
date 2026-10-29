using System.Numerics;
using System.Text.Json.Serialization;
using Engine.Core.Serialization;

namespace Engine.Render;

public sealed record PostProcessSettings
{
    public bool BloomEnabled { get; init; }
    public float BloomIntensity { get; init; } = .12f;
    public float BloomThreshold { get; init; } = 1f;
    public int BloomDownsample { get; init; } = 4;
    public bool GradingEnabled { get; init; }
    public float Contrast { get; init; } = 1f;
    public float Saturation { get; init; } = 1f;
    public SerVec3 ColorFilter { get; init; } = Vector3.One;
    public bool FogEnabled { get; init; }
    public SerVec3 FogColor { get; init; } = new Vector3(.035f,.045f,.055f);
    public float FogDensity { get; init; } = .025f;
    public float FogStart { get; init; } = 4f;
    public float FogBaseHeight { get; init; } = 0f;
    public float FogHeightFalloff { get; init; } = .3f;
    public float FogMaxOpacity { get; init; } = .85f;
    [JsonIgnore] public bool BloomActive => BloomEnabled && BloomIntensity > 0;
    [JsonIgnore] public bool FogActive => FogEnabled && FogDensity > 0 && FogMaxOpacity > 0;
    [JsonIgnore] public bool Active => BloomActive || FogActive || GradingEnabled;

    public PostProcessSettings Sanitize() => this with
    {
        BloomIntensity=Clamp(BloomIntensity,0,1,.12f), BloomThreshold=Clamp(BloomThreshold,.1f,10,1),
        BloomDownsample=BloomDownsample==2?2:4, Contrast=Clamp(Contrast,.5f,1.5f,1), Saturation=Clamp(Saturation,0,2,1),
        ColorFilter=Color(ColorFilter,Vector3.One,2), FogColor=Color(FogColor,new(.035f,.045f,.055f),1),
        FogDensity=Clamp(FogDensity,0,.2f,.025f), FogStart=Clamp(FogStart,0,100,4),
        FogBaseHeight=Clamp(FogBaseHeight,-100,100,0), FogHeightFalloff=Clamp(FogHeightFalloff,0,2,.3f),
        FogMaxOpacity=Clamp(FogMaxOpacity,0,.95f,.85f)
    };
    private static float Clamp(float v,float min,float max,float fallback)=>float.IsFinite(v)?Math.Clamp(v,min,max):fallback;
    private static Vector3 Color(Vector3 v,Vector3 fallback,float max)=>new(
        Clamp(v.X,0,max,fallback.X),Clamp(v.Y,0,max,fallback.Y),Clamp(v.Z,0,max,fallback.Z));
}
