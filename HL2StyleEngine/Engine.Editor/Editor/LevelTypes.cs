using System.Numerics;
using System.Text.Json.Serialization;
using Engine.Core.Serialization;

namespace Engine.Editor.Level;

public sealed class LevelFile
{
    public int Version { get; set; } = 2;

    public List<LevelEntityDef> Entities { get; set; } = new();

    public List<BoxDef>? Boxes { get; set; }
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

public sealed class LevelEntityDef
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Name { get; set; }
    public List<ScriptDef> Scripts { get; set; } = new();

    [JsonPropertyName("Parent")]
    public string? ParentId { get; set; }

    [JsonPropertyName("LocalPosition")]
    public SerVec3 LocalPosition { get; set; } = Vector3.Zero;

    [JsonPropertyName("LocalRotationEulerDeg")]
    public SerVec3 LocalRotationEulerDeg { get; set; } = Vector3.Zero;

    [JsonPropertyName("LocalScale")]
    public SerVec3 LocalScale { get; set; } = Vector3.One;

    [JsonPropertyName("CanPickUp")]
    public bool CanPickUp { get; set; } = false;

    public SerVec3 Size { get; set; } = new(1, 1, 1);
    public SerVec4 Color { get; set; } = new(0.6f, 0.6f, 0.6f, 1f);

    public float YawDeg { get; set; } = 0f;

    public SerVec4 LightColor { get; set; } = new(1f, 1f, 1f, 1f);
    public float Intensity { get; set; } = 3f;
    public float Range { get; set; } = 8f;

    public string MeshPath { get; set; } = "";
    public string MaterialPath { get; set; } = "";

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