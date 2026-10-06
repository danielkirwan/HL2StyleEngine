using System.Numerics;
using System.Text.Json.Serialization;
using Engine.Core.Serialization;
using Engine.Physics.Dynamics;

namespace Engine.Editor.Level;

public sealed class LevelFile
{
    public int Version { get; set; } = 2;
    public bool UsePointLights { get; set; }
    public float Exposure { get; set; } = 1f;
    public float AmbientLight { get; set; } = .22f;
    public float DirectionalLight { get; set; } = 1f;
    public bool EnableShadows { get; set; } = true;
    public LevelStreamingSettings Streaming { get; set; } = new();

    public List<LevelEntityDef> Entities { get; set; } = new();

    public List<BoxDef>? Boxes { get; set; }
    
}

public sealed class LevelStreamingSettings
{
    public bool Enabled { get; set; } = true;
    public float ZoneSize { get; set; } = 32f;
    public int PreloadNeighbours { get; set; } = 1;
    public int BudgetMiB { get; set; } = 256;
    public float RetainSeconds { get; set; } = 15f;
}


public sealed class PrefabFile
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string RootEntityId { get; set; } = "";
    public string BasePrefabPath { get; set; } = "";
    public bool IsVariant { get; set; }
    public List<LevelEntityDef> Entities { get; set; } = new();
}
public static class EntityTypes
{
    public const string Box = "Box";
    public const string PlayerSpawn = "PlayerSpawn";
    public const string PointLight = "PointLight";
    public const string Prop = "Prop";
    public const string TriggerVolume = "TriggerVolume";
    public const string RigidBody = "RigidBody";
}

public sealed class ScriptDef
{
    public string Type { get; set; } = "";   
    public string Json { get; set; } = "{}"; 
}

public sealed class LevelInteractionRewardDef
{
    public string ItemId { get; set; } = "";
    public int Count { get; set; } = 1;
}

public sealed class LevelInteractionDef
{
    public string Kind { get; set; } = "";
    public string StateId { get; set; } = "";
    public string RequiredItem { get; set; } = "";
    public bool ConsumesItem { get; set; }
    public string Prompt { get; set; } = "";
    public string LockedPrompt { get; set; } = "";
    public string SuccessMessage { get; set; } = "";
    public List<string> Targets { get; set; } = new();
    public List<string> RequiredStates { get; set; } = new();
    public string LightGroup { get; set; } = "";
    public List<LevelInteractionRewardDef> Rewards { get; set; } = new();

    public SerVec3 HingeLocalOffset { get; set; } = Vector3.Zero;
    public float OpenAngleDeg { get; set; } = 90f;
    public float LiftHeight { get; set; } = 3f;
    public float PressurePlateMinMass { get; set; } = 8f;
    public float PressurePlateSettleSeconds { get; set; } = 0.35f;
}

public sealed class LevelEntityDef
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Name { get; set; }
    public List<ScriptDef> Scripts { get; set; } = new();
    public LevelInteractionDef? Interaction { get; set; }

    [JsonPropertyName("Parent")]
    public string? ParentId { get; set; }

    public string PrefabAssetPath { get; set; } = "";
    public string PrefabInstanceId { get; set; } = "";
    public string PrefabSourceEntityId { get; set; } = "";
    public bool PrefabUnpacked { get; set; }

    [JsonPropertyName("LocalPosition")]
    public SerVec3 LocalPosition { get; set; } = Vector3.Zero;

    [JsonPropertyName("LocalRotationEulerDeg")]
    public SerVec3 LocalRotationEulerDeg { get; set; } = Vector3.Zero;

    [JsonPropertyName("LocalScale")]
    public SerVec3 LocalScale { get; set; } = Vector3.One;

    [JsonPropertyName("CanPickUp")]
    public bool CanPickUp { get; set; } = false;

    [JsonPropertyName("MotionType")]
    public MotionType MotionType { get; set; } = MotionType.Static;

    public SerVec3 Size { get; set; } = new(1, 1, 1);
    public SerVec4 Color { get; set; } = new(0.6f, 0.6f, 0.6f, 1f);

    public float YawDeg { get; set; } = 0f;

    public SerVec4 LightColor { get; set; } = new(1f, 1f, 1f, 1f);
    public float Intensity { get; set; } = 3f;
    public float Range { get; set; } = 8f;
    public bool LightEnabled { get; set; } = true;
    public bool CastShadows { get; set; }
    public bool IsSpotLight { get; set; }
    public float SpotAngleDeg { get; set; } = 60f;
    public string LightGroup { get; set; } = "";
    public float FlickerAmount { get; set; }
    public float FlickerSpeed { get; set; } = 4f;

    public string MeshPath { get; set; } = "";
    public string MaterialPath { get; set; } = "";

    public bool Damageable { get; set; } = false;
    public float MaxHealth { get; set; } = 100f;
    public List<string> BreakReplacementModelPaths { get; set; } = new();
    public bool BreakReplacementKeepsPhysics { get; set; } = true;
    public List<string> BreakDebrisModelPaths { get; set; } = new();

    public SerVec3 TriggerSize { get; set; } = new(2, 2, 2);
    public string TriggerEvent { get; set; } = "OnEnter";

    public string Shape { get; set; } = "Box";
    public float Mass { get; set; } = 10f;
    public float Friction { get; set; } = 0.8f;
    public float Restitution { get; set; } = 0.05f;
    public bool IsKinematic { get; set; } = false;

    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 1.0f;
}

public sealed class BoxDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Box";

    public SerVec3 Position { get; set; } = new(0, 0, 0);
    public SerVec3 Size { get; set; } = new(1, 1, 1);
    public SerVec4 Color { get; set; } = new(0.6f, 0.6f, 0.6f, 1f);
}

