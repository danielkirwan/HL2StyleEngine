using System.Text.Json;

namespace Engine.Editor.Level;

public static class PrefabIO
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

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
            prefab = JsonSerializer.Deserialize<PrefabFile>(json, Options);
        }
        else
        {
            LevelEntityDef? entity = JsonSerializer.Deserialize<LevelEntityDef>(json, Options);
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
        File.WriteAllText(path, JsonSerializer.Serialize(prefab, Options));
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
