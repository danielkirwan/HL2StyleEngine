using Engine.Editor.Level;
using ImGuiNET;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;

namespace HS2Editor;

internal sealed partial class HS2EditorModule
{
    private void DrawUiPanel()
    {
        ApplyDefaultWindowPlacement("UI Manager");
        ImGui.Begin("UI Manager");
        MarkUiHover();
        Vector2 available = ImGui.GetContentRegionAvail();
        float leftWidth = MathF.Max(220f, available.X * 0.32f);

        ImGui.BeginChild("uiFiles", new Vector2(leftWidth, 0f), ImGuiChildFlags.Borders);
        if (ImGui.Button("New RML")) CreateUiFile("new_ui.rml", "<rml>\n  <body>\n    <div>New UI</div>\n  </body>\n</rml>\n");
        ImGui.SameLine();
        if (ImGui.Button("New RCSS")) CreateUiFile("new_style.rcss", "body {\n  color: #f0e6b0;\n}\n");
        ImGui.Separator();
        foreach (string path in Directory.EnumerateFiles(_uiRoot, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".rml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".rcss", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            bool selected = string.Equals(path, _selectedUiPath, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(Path.GetRelativePath(_uiRoot, path), selected)) LoadUiFile(path);
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("uiEditor", new Vector2(0f, 0f), ImGuiChildFlags.Borders);
        if (string.IsNullOrWhiteSpace(_selectedUiPath))
        {
            ImGui.TextDisabled("Select or create a UI file.");
        }
        else
        {
            ImGui.Text(Path.GetRelativePath(_uiRoot, _selectedUiPath));
            ImGui.SameLine();
            if (_uiDirty) ImGui.TextColored(new Vector4(1f, 0.8f, 0.25f, 1f), "modified");
            ImGui.SameLine();
            if (ImGui.Button("Save UI")) SaveUiFile();
            ImGui.Separator();
            Vector2 editorSize = new(-1f, MathF.Max(180f, ImGui.GetContentRegionAvail().Y * 0.55f));
            if (ImGui.InputTextMultiline("##uiSource", ref _uiSourceText, 128 * 1024, editorSize)) _uiDirty = true;
            ImGui.Separator();
            ImGui.Text("Preview");
            ImGui.BeginChild("uiPreview", new Vector2(0f, 0f), ImGuiChildFlags.Borders);
            ImGui.TextWrapped(_uiSourceText);
            ImGui.EndChild();
        }
        ImGui.EndChild();
        ImGui.End();
    }

    private void LoadLevel(string path)
    {
        path = Path.GetFullPath(path);
        _editingPrefabPath = "";
        _prefabEditReturnLevelPath = "";
        _editor.LoadOrCreate(path, CreateEmptyLevel);
        _project.StartupLevel = MakeProjectRelative(path);
        _project.AddRecentLevel(_project.StartupLevel);
        _renameLevelName = Path.GetFileNameWithoutExtension(path);
        _duplicateLevelName = Path.GetFileNameWithoutExtension(path) + "_copy";
        SaveProject();
        FrameLevel();
        _status = $"Loaded level {MakeProjectRelative(path)}.";
    }

    private bool IsEditingPrefab => !string.IsNullOrWhiteSpace(_editingPrefabPath);

    private void SaveActiveDocument()
    {
        if (IsEditingPrefab)
            SaveEditedPrefab();
        else
            SaveCurrentLevelOnly();
    }

    private void SaveCurrentLevel()
        => SaveActiveDocument();

    private void SaveCurrentLevelOnly()
    {
        string savedPath = Path.GetFullPath(_editor.LevelPath);
        _editor.Save();
        int mirrorCount = MirrorSavedLevelToRuntimeOutputs(savedPath);
        string savedAt = File.GetLastWriteTime(savedPath).ToString("HH:mm:ss");
        _status = mirrorCount > 0
            ? $"Saved level {MakeProjectRelative(savedPath)} at {savedAt}. Updated {mirrorCount} runtime level copy/copies."
            : $"Saved level {MakeProjectRelative(savedPath)} at {savedAt}.";
    }

    private void CreateLevel(string rawName)
    {
        string safeName = MakeSafeFileName(rawName, "new_level");
        string path = Path.Combine(_levelsRoot, safeName + ".json");
        if (File.Exists(path)) { _status = "Level already exists."; return; }
        LevelIO.Save(path, CreateEmptyLevel());
        LoadLevel(path);
    }

    private void DuplicateCurrentLevel()
    {
        string safeName = MakeSafeFileName(_duplicateLevelName, Path.GetFileNameWithoutExtension(_editor.LevelPath) + "_copy");
        string destination = Path.Combine(_levelsRoot, safeName + ".json");
        if (File.Exists(destination)) { _status = "Duplicate destination already exists."; return; }
        _editor.Save();
        File.Copy(_editor.LevelPath, destination);
        LoadLevel(destination);
    }

    private void RenameCurrentLevel()
    {
        string safeName = MakeSafeFileName(_renameLevelName, Path.GetFileNameWithoutExtension(_editor.LevelPath));
        string destination = Path.Combine(_levelsRoot, safeName + ".json");
        if (File.Exists(destination)) { _status = "Rename destination already exists."; return; }
        _editor.Save();
        File.Move(_editor.LevelPath, destination);
        LoadLevel(destination);
    }

    private void SaveSelectedPrefab()
        => SaveSelectedPrefab(isVariant: false);

    private void SaveSelectedPrefab(bool isVariant)
    {
        if (!_editor.TryGetSelectedEntity(out LevelEntityDef entity)) { _status = "Select an entity before creating a prefab."; return; }

        string requestedName = isVariant ? _prefabVariantName : _prefabName;
        string safeName = MakeSafeFileName(requestedName, entity.Name ?? entity.Type);
        string path = Path.Combine(_prefabsRoot, safeName + ".json");
        string basePrefabPath = "";

        if (isVariant)
        {
            if (_editor.TryGetSelectedPrefabInstance(out _, out string selectedInstancePrefab))
                basePrefabPath = selectedInstancePrefab;
            else if (!string.IsNullOrWhiteSpace(_selectedPrefabPath))
                basePrefabPath = ToContentAssetPath(_selectedPrefabPath);

            if (string.IsNullOrWhiteSpace(basePrefabPath))
            {
                _status = "Select a prefab instance or prefab asset before creating a variant.";
                return;
            }
        }

        if (!_editor.TryBuildPrefabFromSelection(safeName, basePrefabPath, isVariant, out PrefabFile prefab))
        {
            _status = "Prefab could not be built from the current selection.";
            return;
        }

        PrefabIO.Save(path, prefab);
        _selectedPrefabPath = path;
        _status = isVariant
            ? $"Prefab variant saved: {MakeProjectRelative(path)}."
            : $"Prefab saved: {MakeProjectRelative(path)}.";
    }

    private void PlacePrefab(string path)
    {
        try
        {
            PrefabFile prefab = PrefabIO.Load(path);
            string assetPath = ToContentAssetPath(path);
            if (!_editor.AddPrefabInstance(prefab, assetPath, "placed"))
            {
                _status = "Prefab could not be placed.";
                return;
            }

            _selectedPrefabPath = path;
            _status = $"Placed prefab {Path.GetFileNameWithoutExtension(path)}.";
        }
        catch (Exception ex) { _status = $"Prefab load failed: {ex.Message}"; }
    }

    private void ApplySelectedPrefabInstance()
    {
        if (!_editor.TryBuildPrefabFromSelectedInstanceForApply("", out PrefabFile prefab, out string assetPath))
        {
            _status = "Select a prefab instance before applying changes.";
            return;
        }

        string path = ResolveContentAssetPath(assetPath);
        try
        {
            PrefabFile existing = File.Exists(path) ? PrefabIO.Load(path) : prefab;
            prefab.Id = existing.Id;
            prefab.Name = existing.Name;
            prefab.BasePrefabPath = existing.BasePrefabPath;
            prefab.IsVariant = existing.IsVariant;
            PrefabIO.Save(path, prefab);
            _selectedPrefabPath = path;
            _status = $"Applied selected instance to {MakeProjectRelative(path)}.";
        }
        catch (Exception ex) { _status = $"Prefab apply failed: {ex.Message}"; }
    }

    private void RevertSelectedPrefabInstance()
    {
        if (!_editor.TryGetSelectedPrefabInstance(out _, out string assetPath))
        {
            _status = "Select a prefab instance before reverting.";
            return;
        }

        string path = ResolveContentAssetPath(assetPath);
        try
        {
            PrefabFile prefab = PrefabIO.Load(path);
            if (_editor.RevertSelectedPrefabInstance(prefab, assetPath))
                _status = $"Reverted selected instance from {MakeProjectRelative(path)}.";
            else
                _status = "Prefab instance could not be reverted.";
        }
        catch (Exception ex) { _status = $"Prefab revert failed: {ex.Message}"; }
    }

    private void UnpackSelectedPrefabInstance()
    {
        if (_editor.UnpackSelectedPrefabInstance())
            _status = "Prefab instance unpacked into normal scene objects.";
        else
            _status = "Select a prefab instance before unpacking.";
    }

    private void EditPrefab(string path)
    {
        try
        {
            if (!IsEditingPrefab)
            {
                if (_editor.Dirty)
                    _editor.Save();
                _prefabEditReturnLevelPath = _editor.LevelPath;
            }

            PrefabFile prefab = PrefabIO.Load(path);
            var level = new LevelFile
            {
                Version = 2,
                Entities = CloneEntities(prefab.Entities)
            };

            _editingPrefabPath = Path.GetFullPath(path);
            _selectedPrefabPath = _editingPrefabPath;
            _editor.LoadFromMemory(_editingPrefabPath, level);
            _status = $"Editing prefab {MakeProjectRelative(_editingPrefabPath)}. Use Save Prefab, then Return To Level.";
        }
        catch (Exception ex) { _status = $"Prefab edit failed: {ex.Message}"; }
    }

    private void SaveEditedPrefab()
    {
        if (!IsEditingPrefab)
            return;

        try
        {
            PrefabFile existing = File.Exists(_editingPrefabPath) ? PrefabIO.Load(_editingPrefabPath) : new PrefabFile();
            List<LevelEntityDef> entities = CloneEntities(_editor.LevelFile.Entities);
            foreach (LevelEntityDef entity in entities)
            {
                entity.PrefabAssetPath = "";
                entity.PrefabInstanceId = "";
                entity.PrefabSourceEntityId = "";
                entity.PrefabUnpacked = false;
            }

            string rootId = entities.FirstOrDefault(entity => string.IsNullOrWhiteSpace(entity.ParentId))?.Id
                ?? entities.FirstOrDefault()?.Id
                ?? "";
            var prefab = new PrefabFile
            {
                Id = existing.Id,
                Name = existing.Name,
                RootEntityId = rootId,
                BasePrefabPath = existing.BasePrefabPath,
                IsVariant = existing.IsVariant,
                Entities = entities
            };

            PrefabIO.Save(_editingPrefabPath, prefab);
            _editor.MarkClean();
            _status = $"Saved prefab {MakeProjectRelative(_editingPrefabPath)}.";
        }
        catch (Exception ex) { _status = $"Prefab save failed: {ex.Message}"; }
    }

    private void ReturnFromPrefabEdit()
    {
        if (!IsEditingPrefab)
            return;

        string returnLevel = _prefabEditReturnLevelPath;
        _editingPrefabPath = "";
        _prefabEditReturnLevelPath = "";
        if (!string.IsNullOrWhiteSpace(returnLevel) && File.Exists(returnLevel))
            LoadLevel(returnLevel);
    }

    private static List<LevelEntityDef> CloneEntities(IEnumerable<LevelEntityDef> source)
        => source.Select(CloneEntity).ToList();

    private static LevelEntityDef CloneEntity(LevelEntityDef source)
        => JsonSerializer.Deserialize<LevelEntityDef>(JsonSerializer.Serialize(source, EditorJson.Options), EditorJson.Options) ?? new LevelEntityDef();

    private void CreateUiFile(string fileName, string defaultText)
    {
        string path = Path.Combine(_uiRoot, fileName);
        if (!File.Exists(path)) File.WriteAllText(path, defaultText);
        LoadUiFile(path);
    }

    private void LoadUiFile(string path)
    {
        if (_uiDirty) SaveUiFile();
        _selectedUiPath = path;
        _uiSourceText = File.Exists(path) ? File.ReadAllText(path) : "";
        _uiDirty = false;
    }

    private void SaveUiFile()
    {
        if (string.IsNullOrWhiteSpace(_selectedUiPath)) return;
        File.WriteAllText(_selectedUiPath, _uiSourceText);
        _uiDirty = false;
        _status = $"Saved UI file {MakeProjectRelative(_selectedUiPath)}.";
    }

    private void LaunchAssetImporter()
    {
        StartDotnetProject(ToAbsolutePath(_project.AssetImporterProject), []);
        _status = "Launching asset importer.";
    }

    private void LaunchGameFromCurrentLevel()
    {
        if (IsEditingPrefab)
        {
            SaveEditedPrefab();
            _status = "Prefab saved. Return to the level before launching play.";
            return;
        }

        SaveCurrentLevelOnly();
        StartDotnetProject(ToAbsolutePath(_project.GameProject), ["--", "--level", _editor.LevelPath]);
        _status = $"Launching game from {MakeProjectRelative(_editor.LevelPath)}.";
    }

    private void StartDotnetProject(string projectPath, IReadOnlyList<string> extraArgs)
    {
        try
        {
            var info = new ProcessStartInfo("dotnet") { WorkingDirectory = _projectRoot, UseShellExecute = false };
            info.ArgumentList.Add("run");
            info.ArgumentList.Add("--project");
            info.ArgumentList.Add(projectPath);
            foreach (string arg in extraArgs) info.ArgumentList.Add(arg);
            Process.Start(info);
        }
        catch (Exception ex) { _status = $"Launch failed: {ex.Message}"; }
    }
}




