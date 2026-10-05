using System.Text.Json;
using Engine.Core.Serialization;

namespace Engine.Editor.Level;

public static class PrefabIO
{
    public static PrefabFile Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Prefab file not found: {path}");

        string json = File.ReadAllText(path);
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        PrefabFile? prefab = null;
        if (doc.RootElement.TryGetProperty(nameof(PrefabFile.Entities), out JsonElement entitiesElement) &&
            entitiesElement.ValueKind == JsonValueKind.Array)
        {
            prefab = StructuredData.ReadJson<PrefabFile>(System.Text.Encoding.UTF8.GetBytes(json));
        }
        else
        {
            LevelEntityDef? entity = StructuredData.ReadJson<LevelEntityDef>(System.Text.Encoding.UTF8.GetBytes(json));
            if (entity != null)
            {
                prefab = new PrefabFile
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    RootEntityId = entity.Id,
                    Entities = [entity]
                };
            }
        }

        if (prefab == null)
            throw new InvalidDataException($"Failed to deserialize prefab: {path}");

        Fixup(prefab, path);
        return prefab;
    }

    public static void Save(string path, PrefabFile prefab)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        Fixup(prefab, path);
        StructuredData.Save(path, prefab);
    }

    private static void Fixup(PrefabFile prefab, string path)
    {
        prefab.Entities ??= new();
        if (string.IsNullOrWhiteSpace(prefab.Id))
            prefab.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(prefab.Name))
            prefab.Name = Path.GetFileNameWithoutExtension(path);
        if (prefab.Entities.Count > 0 && string.IsNullOrWhiteSpace(prefab.RootEntityId))
            prefab.RootEntityId = prefab.Entities[0].Id;

        foreach (LevelEntityDef entity in prefab.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.Id))
                entity.Id = Guid.NewGuid().ToString("N");

            entity.PrefabAssetPath = "";
            entity.PrefabInstanceId = "";
            entity.PrefabSourceEntityId = "";
            entity.PrefabUnpacked = false;
        }
    }
}
