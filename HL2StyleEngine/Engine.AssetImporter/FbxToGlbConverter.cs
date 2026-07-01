using System.Diagnostics;
using System.Text;

namespace Engine.AssetImporter;

internal static class FbxToGlbConverter
{
    private const int MinSupportedBlenderFbxVersion = 7100;

    public static async Task<FbxConversionResult> ConvertAsync(FbxImportRequest request, CancellationToken cancellationToken = default)
    {
        StringBuilder log = new();

        if (!Directory.Exists(request.SourceFolder))
            return Fail($"Source folder does not exist: {request.SourceFolder}");

        string? blender = ResolveBlenderExe(request.BlenderExePath);
        if (blender == null)
            return Fail("Could not find Blender. Browse to blender.exe or set HS2_BLENDER_EXE.");

        List<string> fbxPaths = Directory
            .EnumerateFiles(request.SourceFolder, "*.fbx", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (fbxPaths.Count == 0)
            return Fail("No .fbx file was found in the selected source folder.");

        if (!request.ConvertAllFbx)
            fbxPaths = [fbxPaths[0]];

        Directory.CreateDirectory(request.DestinationFolder);
        string scriptPath = Path.Combine(Path.GetTempPath(), $"hs2_fbx_to_glb_{Guid.NewGuid():N}.py");
        string blenderScript = request.ImportMode == FbxImportMode.Animation
            ? BlenderAnimationExportScript
            : BlenderExportScript;
        await File.WriteAllTextAsync(scriptPath, blenderScript, cancellationToken);

        List<string> outputPaths = new();
        int failedCount = 0;
        int skippedCount = 0;

        try
        {
            if (request.ConvertAllFbx)
                log.AppendLine($"Batch {request.ImportMode.ToString().ToLowerInvariant()} conversion: {fbxPaths.Count} FBX files.");

            HashSet<string> usedOutputNames = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fbxPaths.Count; i++)
            {
                string fbxPath = fbxPaths[i];
                string outputName = GetOutputName(request, fbxPath, multipleFiles: fbxPaths.Count > 1);
                outputName = MakeUniqueOutputName(outputName, usedOutputNames);
                string outputPath = Path.Combine(request.DestinationFolder, outputName + ".glb");
                string textureSearchRoot = ResolveTextureSearchRoot(request.SourceFolder, fbxPath);
                string fbxHint = Path.GetFileNameWithoutExtension(fbxPath);

                if (request.ConvertAllFbx)
                    log.AppendLine($"[{i + 1}/{fbxPaths.Count}] {Path.GetFileName(fbxPath)}");

                if (TryReadFbxVersion(fbxPath, out int fbxVersion) && fbxVersion > 0 && fbxVersion < MinSupportedBlenderFbxVersion)
                {
                    skippedCount++;
                    log.AppendLine($"Skipped unsupported FBX version {fbxVersion}. Blender requires FBX {MinSupportedBlenderFbxVersion} or later; re-export this file from Unity, Autodesk FBX Converter, or a DCC tool before importing.");
                    if (!request.ConvertAllFbx)
                        break;
                    continue;
                }

                bool success = await ConvertOneAsync(
                    blender,
                    request.SourceFolder,
                    scriptPath,
                    fbxPath,
                    outputPath,
                    textureSearchRoot,
                    fbxHint,
                    request.ImportMode,
                    log,
                    cancellationToken);

                if (success)
                {
                    outputPaths.Add(outputPath);
                    continue;
                }

                failedCount++;
                if (!request.ConvertAllFbx)
                    break;
            }

            if (outputPaths.Count == 0)
            {
                if (skippedCount > 0 && failedCount == 0)
                    log.AppendLine($"No GLB output was produced. {skippedCount}/{fbxPaths.Count} FBX files were skipped because Blender cannot import their FBX version.");
                else
                    log.AppendLine("No GLB output was produced.");

                return new FbxConversionResult(false, null, log.ToString(), outputPaths);
            }

            if (failedCount == 0 && skippedCount == 0)
                log.AppendLine("Conversion complete.");
            else if (failedCount == 0)
                log.AppendLine($"Conversion complete with skipped files. {outputPaths.Count}/{fbxPaths.Count} GLB files were produced; {skippedCount} file(s) were skipped.");
            else
                log.AppendLine($"Conversion finished with failures. {outputPaths.Count}/{fbxPaths.Count} GLB files were produced; {failedCount} failed and {skippedCount} were skipped.");

            return new FbxConversionResult(failedCount == 0, outputPaths[0], log.ToString(), outputPaths);
        }
        catch (Exception ex)
        {
            log.AppendLine(ex.ToString());
            return new FbxConversionResult(false, outputPaths.FirstOrDefault(), log.ToString(), outputPaths);
        }
        finally
        {
            TryDelete(scriptPath);
        }

        FbxConversionResult Fail(string message)
        {
            log.AppendLine(message);
            return new FbxConversionResult(false, null, log.ToString());
        }
    }

    private static async Task<bool> ConvertOneAsync(
        string blender,
        string workingDirectory,
        string scriptPath,
        string fbxPath,
        string outputPath,
        string textureSearchRoot,
        string fbxHint,
        FbxImportMode importMode,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        log.AppendLine($"Blender: {blender}");
        log.AppendLine($"FBX: {fbxPath}");
        if (importMode == FbxImportMode.Model)
            log.AppendLine($"Texture search root: {textureSearchRoot}");
        else
            log.AppendLine("Animation export: preserving armatures, skins, actions, and clips where Blender exposes them.");
        log.AppendLine($"Output: {outputPath}");

        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = blender,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.StartInfo.ArgumentList.Add("--background");
        process.StartInfo.ArgumentList.Add("--python");
        process.StartInfo.ArgumentList.Add(scriptPath);
        process.StartInfo.ArgumentList.Add("--");
        process.StartInfo.ArgumentList.Add(fbxPath);
        process.StartInfo.ArgumentList.Add(outputPath);
        process.StartInfo.ArgumentList.Add(textureSearchRoot);
        process.StartInfo.ArgumentList.Add(fbxHint);

        process.Start();
        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(stdout))
            log.AppendLine(stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr))
            log.AppendLine(stderr.TrimEnd());

        if (process.ExitCode != 0)
        {
            log.AppendLine($"Blender exited with code {process.ExitCode}.");
            return false;
        }

        if (!File.Exists(outputPath))
        {
            log.AppendLine("Blender finished but no GLB output was produced.");
            return false;
        }

        log.AppendLine($"Ready: {outputPath}");
        return true;
    }

    public static string? ResolveBlenderExe(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return explicitPath;

        string? envPath = Environment.GetEnvironmentVariable("HS2_BLENDER_EXE");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return envPath;

        string? fromPath = FindOnPath("blender.exe");
        if (fromPath != null)
            return fromPath;

        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Blender Foundation");
        if (Directory.Exists(root))
        {
            string? found = Directory
                .EnumerateFiles(root, "blender.exe", SearchOption.AllDirectories)
                .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (found != null)
                return found;
        }

        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (string entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate = Path.Combine(entry, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string GetOutputName(FbxImportRequest request, string fbxPath, bool multipleFiles)
    {
        if (multipleFiles)
            return SanitizeFileName(Path.GetFileNameWithoutExtension(fbxPath));

        string outputName = SanitizeFileName(request.OutputName);
        return string.IsNullOrWhiteSpace(outputName)
            ? SanitizeFileName(Path.GetFileNameWithoutExtension(fbxPath))
            : outputName;
    }

    private static string MakeUniqueOutputName(string outputName, HashSet<string> usedOutputNames)
    {
        string safeName = string.IsNullOrWhiteSpace(outputName) ? "model" : outputName;
        if (usedOutputNames.Add(safeName))
            return safeName;

        int suffix = 2;
        while (!usedOutputNames.Add($"{safeName}_{suffix}"))
            suffix++;

        return $"{safeName}_{suffix}";
    }

    private static string SanitizeFileName(string value)
    {
        string name = Path.GetFileNameWithoutExtension(value.Trim());
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return name;
    }

    private static bool TryReadFbxVersion(string fbxPath, out int version)
    {
        version = 0;
        try
        {
            byte[] buffer = new byte[8192];
            using FileStream stream = File.OpenRead(fbxPath);
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                return false;

            if (read >= 27)
            {
                string binaryHeader = Encoding.ASCII.GetString(buffer, 0, Math.Min(21, read));
                if (binaryHeader.StartsWith("Kaydara FBX Binary", StringComparison.Ordinal))
                {
                    version = BitConverter.ToInt32(buffer, 23);
                    return true;
                }
            }

            string text = Encoding.ASCII.GetString(buffer, 0, read);
            int index = text.IndexOf("FBXVersion", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            while (index < text.Length && !char.IsDigit(text[index]))
                index++;

            int start = index;
            while (index < text.Length && char.IsDigit(text[index]))
                index++;

            return index > start && int.TryParse(text[start..index], out version);
        }
        catch
        {
            version = 0;
            return false;
        }
    }
    private static string ResolveTextureSearchRoot(string selectedSourceFolder, string fbxPath)
    {
        DirectoryInfo selected = new(selectedSourceFolder);
        if (string.Equals(selected.Name, "FBX", StringComparison.OrdinalIgnoreCase) && selected.Parent != null)
            return selected.Parent.FullName;

        if (ContainsTextureFiles(selectedSourceFolder))
            return selectedSourceFolder;

        DirectoryInfo? fbxDirectory = Directory.GetParent(fbxPath);
        if (fbxDirectory != null &&
            string.Equals(fbxDirectory.Name, "FBX", StringComparison.OrdinalIgnoreCase) &&
            fbxDirectory.Parent != null)
        {
            return fbxDirectory.Parent.FullName;
        }

        DirectoryInfo? parent = selected.Parent;
        if (parent != null && ContainsTextureFiles(parent.FullName))
            return parent.FullName;

        return selectedSourceFolder;
    }

    private static bool ContainsTextureFiles(string folder)
    {
        if (!Directory.Exists(folder))
            return false;

        string[] extensions = [".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff"];
        return Directory
            .EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Any(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temp script cleanup is best-effort only.
        }
    }

    private const string BlenderExportScript = """
import bpy
import os
import re
import sys
import traceback

args = sys.argv[sys.argv.index("--") + 1:]
fbx_path = args[0]
output_path = args[1]
source_root = args[2]
fbx_hint = args[3] if len(args) > 3 else os.path.splitext(os.path.basename(fbx_path))[0]
max_texture_size = 1024

empty_hints = set(("", "material", "default", "none", "imported_material", "imported_weapon_material"))

def clean_hint(value):
    if not value:
        return ""
    return os.path.splitext(str(value))[0].lower().replace("-", "_").replace(" ", "_")

def add_hint(hints, value):
    hint = clean_hint(value)
    if hint and hint not in empty_hints and hint not in hints:
        hints.append(hint)

    match = re.search(r"damaged_?crate_?(\d+)$|damagedcrates?(\d+)$", hint)
    if match:
        number_text = match.group(1) or match.group(2)
        try:
            number = int(number_text)
            grouped = "damagedcrates1to4" if number <= 4 else "damagedcrates5to7"
            if grouped not in hints:
                hints.append(grouped)
        except ValueError:
            pass


def build_hints(material_name):
    hints = []
    add_hint(hints, material_name)
    add_hint(hints, fbx_hint)
    return hints

def material_match_score(file_name, material_hint):
    if not material_hint:
        return 0

    stem = clean_hint(file_name)
    hint = clean_hint(material_hint)
    tokens = [token for token in stem.split("_") if token]

    if stem == hint:
        return 120
    if stem.startswith(hint + "_"):
        return 100
    if hint in tokens:
        return 60
    if hint in stem:
        return 35
    return 0

def find_texture(keywords, material_hint=None, exclude_keywords=(), allow_generic=True):
    material_matches = []
    generic_matches = []
    for root, _, files in os.walk(source_root):
        for name in files:
            lower = name.lower()
            if not lower.endswith((".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff")):
                continue
            if any(k in lower for k in exclude_keywords):
                continue
            if not any(k in lower for k in keywords):
                continue

            path = os.path.join(root, name)
            score = material_match_score(name, material_hint)
            if score > 0:
                material_matches.append((-score, path))
            else:
                generic_matches.append(path)

    if material_matches:
        material_matches.sort()
        return material_matches[0][1]
    if material_hint and not allow_generic:
        return None
    if material_hint and len(generic_matches) > 1:
        return None

    generic_matches.sort()
    return generic_matches[0] if generic_matches else None

def find_texture_for_hints(keywords, hints, exclude_keywords=(), allow_generic=True):
    for hint in hints:
        texture = find_texture(keywords, hint, exclude_keywords, allow_generic=False)
        if texture:
            return texture
    return find_texture(keywords, None, exclude_keywords, allow_generic)

def load_texture_image(image_path, color_space):
    if not image_path:
        return None

    image = bpy.data.images.load(image_path, check_existing=True)
    width, height = image.size
    largest = max(width, height)
    if largest > max_texture_size:
        scale = max_texture_size / largest
        image.scale(max(1, int(width * scale)), max(1, int(height * scale)))
        image.pack()

    image.colorspace_settings.name = color_space
    return image

def attach_texture(nodes, links, shader, input_name, image_path, color_space):
    if not image_path or input_name not in shader.inputs:
        return
    image = load_texture_image(image_path, color_space)
    if image is None:
        return
    tex = nodes.new(type="ShaderNodeTexImage")
    tex.image = image
    links.new(tex.outputs["Color"], shader.inputs[input_name])

def attach_normal(nodes, links, shader, image_path):
    if not image_path or "Normal" not in shader.inputs:
        return
    image = load_texture_image(image_path, "Non-Color")
    if image is None:
        return
    tex = nodes.new(type="ShaderNodeTexImage")
    tex.image = image
    normal = nodes.new(type="ShaderNodeNormalMap")
    links.new(tex.outputs["Color"], normal.inputs["Color"])
    links.new(normal.outputs["Normal"], shader.inputs["Normal"])

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()
bpy.ops.import_scene.fbx(filepath=fbx_path)

def material_textures(material_name):
    hints = build_hints(material_name)
    albedo = find_texture_for_hints(("albedo", "basecolor", "base_color", "diffuse", "_color", "color"), hints, ("emission", "emissive", "normal", "metallic", "metalness", "rough", "opacity"))
    normal = find_texture_for_hints(("normal", "norm"), hints)
    emission = find_texture_for_hints(("emission", "emissive"), hints, allow_generic=False)
    metallic = find_texture_for_hints(("metallic", "metalness"), hints)
    roughness = find_texture_for_hints(("roughness", "rough"), hints)
    print("Material texture match:", material_name, "hints=", hints, "albedo=", albedo, "normal=", normal, "metallic=", metallic, "roughness=", roughness)
    return albedo, normal, emission, metallic, roughness

def setup_material(mat):
    albedo, normal, emission, metallic, roughness = material_textures(mat.name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    shader = nodes.get("Principled BSDF")
    if shader is None:
        return
    attach_texture(nodes, links, shader, "Base Color", albedo, "sRGB")
    attach_normal(nodes, links, shader, normal)
    attach_texture(nodes, links, shader, "Emission Color", emission, "sRGB")
    attach_texture(nodes, links, shader, "Metallic", metallic, "Non-Color")
    attach_texture(nodes, links, shader, "Roughness", roughness, "Non-Color")

for mat in bpy.data.materials:
    setup_material(mat)

if len(bpy.data.materials) == 0:
    mat = bpy.data.materials.new(name=fbx_hint or "Imported_Material")
    setup_material(mat)
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            obj.data.materials.append(mat)
else:
    first_material = bpy.data.materials[0]
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH" and len(obj.data.materials) == 0:
            obj.data.materials.append(first_material)

os.makedirs(os.path.dirname(output_path), exist_ok=True)
temp_output_path = output_path + ".tmp.glb"
if os.path.exists(temp_output_path):
    os.remove(temp_output_path)

try:
    bpy.ops.export_scene.gltf(
        filepath=temp_output_path,
        export_format="GLB",
        export_texcoords=True,
        export_normals=True,
        export_materials="EXPORT",
        export_apply=True,
        export_yup=True)
    os.replace(temp_output_path, output_path)
except Exception:
    traceback.print_exc()
    if os.path.exists(temp_output_path):
        os.remove(temp_output_path)
    sys.exit(1)
""";

    private const string BlenderAnimationExportScript = """
import bpy
import os
import sys
import traceback

args = sys.argv[sys.argv.index("--") + 1:]
fbx_path = args[0]
output_path = args[1]
fbx_hint = args[3] if len(args) > 3 else os.path.splitext(os.path.basename(fbx_path))[0]

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()
bpy.ops.import_scene.fbx(filepath=fbx_path)

clip_name = fbx_hint or os.path.splitext(os.path.basename(fbx_path))[0]

for action in bpy.data.actions:
    if not action.name or action.name.startswith("Armature") or action.name.startswith("mixamo.com"):
        action.name = clip_name
    action.use_fake_user = True

if bpy.context.scene.frame_end <= bpy.context.scene.frame_start:
    max_end = bpy.context.scene.frame_start
    for action in bpy.data.actions:
        max_end = max(max_end, int(action.frame_range[1]))
    bpy.context.scene.frame_end = max_end

print("Imported objects:", [(obj.name, obj.type) for obj in bpy.context.scene.objects])
print("Actions:", [(action.name, tuple(action.frame_range)) for action in bpy.data.actions])
print("Armatures:", [obj.name for obj in bpy.context.scene.objects if obj.type == "ARMATURE"])

os.makedirs(os.path.dirname(output_path), exist_ok=True)
temp_output_path = output_path + ".tmp.glb"
if os.path.exists(temp_output_path):
    os.remove(temp_output_path)

def supported_export_kwargs(values):
    properties = bpy.ops.export_scene.gltf.get_rna_type().properties
    lookup = {prop.identifier: prop for prop in properties}
    kwargs = {}
    for key, value in values.items():
        prop = lookup.get(key)
        if prop is None:
            continue
        enum_items = getattr(prop, "enum_items", None)
        if enum_items and isinstance(value, str):
            allowed = [item.identifier for item in enum_items]
            if allowed and value not in allowed:
                print("Skipping unsupported glTF export enum", key, value, "allowed=", allowed)
                continue
        kwargs[key] = value
    return kwargs

try:
    kwargs = supported_export_kwargs({
        "filepath": temp_output_path,
        "export_format": "GLB",
        "export_yup": True,
        "export_apply": False,
        "export_texcoords": True,
        "export_normals": True,
        "export_materials": "EXPORT",
        "export_animations": True,
        "export_skins": True,
        "export_morph": True,
        "export_force_sampling": True,
        "export_frame_range": True,
        "export_frame_step": 1,
        "export_nla_strips": True,
        "export_all_actions": True,
        "export_animation_mode": "ACTIONS",
        "export_bake_animation": True,
        "export_def_bones": True,
        "export_hierarchy_flatten_bones": False,
        "use_selection": False
    })
    bpy.ops.export_scene.gltf(**kwargs)
    os.replace(temp_output_path, output_path)
except Exception:
    traceback.print_exc()
    if os.path.exists(temp_output_path):
        os.remove(temp_output_path)
    sys.exit(1)
""";
}
