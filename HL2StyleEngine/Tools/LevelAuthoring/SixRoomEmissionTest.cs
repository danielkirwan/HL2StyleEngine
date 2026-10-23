using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Engine.Editor.Level;
using Engine.Physics.Dynamics;

internal static class SixRoomEmissionTest
{
    internal const string FileName = "sixRoomEmissionTest.json";

    internal static void Build(string root)
    {
        string path = Path.Combine(root, "Game/Content/Levels", FileName);
        if (File.Exists(path)) throw new IOException("Emission test already exists; edit it in HS2Editor. Existing levels are never overwritten by this command.");
        var level = LevelIO.Load(Path.Combine(root, "Game/Content/Levels/sixRoomFlashlightTest.json"));
        level.UsePointLights = true;
        string Id(string name) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes("emission-test/" + name))).ToLowerInvariant();
        const string modelRoot = "Content/Models/ViewModels/";
        var light = new LevelEntityDef
        {
            Id = Id("light"), Name = "EmissionTest_ScreenLight", Type = EntityTypes.PointLight,
            LocalPosition = new(0, 2.4f, -10), LightColor = new(.3f, .65f, 1, 1),
            Intensity = 2.5f, Range = 6, CastShadows = true, LightGroup = "EmissionTest"
        };
        level.Entities.Add(light);
        level.Entities.Add(new()
        {
            Id = Id("console"), Name = "EmissionTest_Console", Type = EntityTypes.Prop,
            LocalPosition = new(0, 1.35f, -9.5f), Size = new(1.4f, .7f, 1.05f), LocalRotationEulerDeg = new(0, 270, 0),
            MeshPath = modelRoot + "Console 1.glb", Color = Vector4.One,
            EmissionIntensity = 3, EmissionLightId = light.Id
        });
        level.Entities.Add(new()
        {
            Id = Id("stand"), Name = "EmissionTest_ConsoleStand", Type = EntityTypes.RigidBody,
            LocalPosition = new(0, .5f, -9.5f), Size = new(1.2f, 1, .85f),
            MeshPath = modelRoot + "Breakable_Wooden_Crate.glb", Color = Vector4.One,
            MotionType = MotionType.Static, Shape = "Box", Mass = 0, Restitution = 0
        });
        level.Entities.Add(new()
        {
            Id = Id("arcade"), Name = "EmissionTest_Arcade", Type = EntityTypes.RigidBody,
            LocalPosition = new(2, 1, -9.5f), Size = new(1.1f, 2, .95f), LocalRotationEulerDeg = new(0, 180, 0),
            MeshPath = modelRoot + "Arcade_Machine_1.glb", Color = Vector4.One,
            MotionType = MotionType.Static, Shape = "Box", Mass = 0,
            EmissionIntensity = 2, EmissionLightId = light.Id
        });
        level.Entities.Add(new()
        {
            Id = Id("switch"), Name = "EmissionTest_PowerSwitch", Type = EntityTypes.Prop,
            LocalPosition = new(-1.2f, 1.3f, -10), Size = new(.3f, .55f, .3f),
            MeshPath = modelRoot + "Lever04.glb", Color = Vector4.One,
            Interaction = new() { Kind = "LightSwitch", Targets = [light.Id] }
        });
        LevelIO.Save(path, level);
        Console.WriteLine($"Created {path}. Console/arcade and E-operated power lever ahead of spawn; original levels unchanged.");
    }
}
