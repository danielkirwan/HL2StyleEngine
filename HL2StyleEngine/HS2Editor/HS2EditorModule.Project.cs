using Engine.Editor.Level;
using Game.World;
using System.Numerics;

namespace HS2Editor;

internal sealed partial class HS2EditorModule
{
    private void EnsureProjectFolders()
    {
        string[] folders =
        [
            _contentRoot,
            _levelsRoot,
            Path.Combine(_contentRoot, "Models"),
            Path.Combine(_contentRoot, "Materials"),
            Path.Combine(_contentRoot, "Animations"),
            Path.Combine(_contentRoot, "Scripts"),
            _uiRoot,
            _prefabsRoot
        ];

        foreach (string folder in folders)
            Directory.CreateDirectory(folder);
    }

    private void EnsureStarterLevels()
    {
        string interaction = Path.Combine(_levelsRoot, "interaction_test.json");
        if (!File.Exists(interaction))
            LevelIO.Save(interaction, SimpleLevel.BuildInteractionTestFile());

        string blockout = Path.Combine(_levelsRoot, "interaction_test_blockout.json");
        if (!File.Exists(blockout))
            LevelIO.Save(blockout, SimpleLevel.BuildInteractionTestBlockoutFile());
    }

    private static LevelFile CreateEmptyLevel()
    {
        var level = new LevelFile { Version = 2 };
        level.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.PlayerSpawn,
            Name = "PlayerSpawn",
            LocalPosition = new Vector3(0f, 0f, -5f),
            YawDeg = 0f
        });
        level.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.PointLight,
            Name = "Light_Main",
            LocalPosition = new Vector3(0f, 4f, -2f),
            LightColor = new Vector4(1f, 0.94f, 0.76f, 1f),
            Intensity = 3f,
            Range = 10f
        });
        level.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.Box,
            Name = "Floor",
            LocalPosition = new Vector3(0f, -0.1f, 0f),
            Size = new Vector3(12f, 0.2f, 12f),
            Color = new Vector4(0.18f, 0.19f, 0.20f, 1f)
        });
        return level;
    }

    private void FrameLevel()
    {
        if (_editor.TryGetPlayerSpawn(out Vector3 spawn, out float yaw))
        {
            _cameraPosition = spawn + new Vector3(0f, 3.5f, -8f);
            _cameraYaw = yaw * MathF.PI / 180f;
            _cameraPitch = -0.28f;
        }
    }

    private bool FileMatchesContentFilter(string path)
    {
        if (string.IsNullOrWhiteSpace(_contentFilter))
            return true;
        return Path.GetFileName(path).Contains(_contentFilter, StringComparison.OrdinalIgnoreCase) ||
               MakeProjectRelative(path).Contains(_contentFilter, StringComparison.OrdinalIgnoreCase);
    }

    private string ToContentAssetPath(string absolutePath)
        => "Content/" + Path.GetRelativePath(_contentRoot, absolutePath).Replace('\\', '/');

    private string MakeProjectRelative(string path)
    {
        string absolute = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(_projectRoot, absolute).Replace('\\', '/');
        return relative == "." ? "" : relative;
    }

    private string ToAbsolutePath(string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(_projectRoot, path.Replace('/', Path.DirectorySeparatorChar)));
    }

    private void SaveProject()
    {
        _project.ContentRoot = MakeProjectRelative(_contentRoot);
        _project.Save(_projectFilePath);
    }

    private void SaveProjectAndActiveDocument()
    {
        string documentStatus = "";
        if (IsEditingPrefab)
        {
            SaveEditedPrefab();
            documentStatus = _status;
        }
        else if (_editor.LevelFile != null)
        {
            SaveCurrentLevelOnly();
            documentStatus = _status;
        }

        SaveProject();
        _status = string.IsNullOrWhiteSpace(documentStatus)
            ? "Project settings saved."
            : $"{documentStatus} Project settings saved.";
    }

    private int MirrorSavedLevelToRuntimeOutputs(string savedLevelPath)
    {
        savedLevelPath = Path.GetFullPath(savedLevelPath);
        string levelsRoot = Path.GetFullPath(_levelsRoot);
        if (!IsSameOrChildPath(levelsRoot, savedLevelPath))
            return 0;

        string relativeLevelPath = Path.GetRelativePath(levelsRoot, savedLevelPath);
        int copied = 0;
        foreach (string runtimeLevelsRoot in EnumerateRuntimeLevelOutputDirectories())
        {
            string destination = Path.GetFullPath(Path.Combine(runtimeLevelsRoot, relativeLevelPath));
            if (string.Equals(destination, savedLevelPath, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                string? destinationDirectory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);
                File.Copy(savedLevelPath, destination, overwrite: true);
                copied++;
            }
            catch
            {
                // Saving the source level is the important part; stale output copies are repaired on the next build.
            }
        }

        return copied;
    }

    private IEnumerable<string> EnumerateRuntimeLevelOutputDirectories()
    {
        foreach (string projectName in new[] { "Game", "HS2Editor" })
        {
            string binRoot = Path.Combine(_projectRoot, projectName, "bin");
            if (!Directory.Exists(binRoot))
                continue;

            foreach (string levelsDirectory in Directory.EnumerateDirectories(binRoot, "Levels", SearchOption.AllDirectories))
            {
                DirectoryInfo? parent = Directory.GetParent(levelsDirectory);
                if (parent != null && string.Equals(parent.Name, "Content", StringComparison.OrdinalIgnoreCase))
                    yield return levelsDirectory;
            }
        }
    }

    private static bool IsSameOrChildPath(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }

    private static string MakeSafeFileName(string value, string fallback)
    {
        string name = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private string FindProjectRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? dir = new(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "HL2StyleEngine.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        return Directory.GetCurrentDirectory();
    }
}

