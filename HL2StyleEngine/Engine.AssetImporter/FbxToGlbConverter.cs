using System.Diagnostics;
using System.Text;

namespace Engine.AssetImporter;

internal static class FbxToGlbConverter
{
    public static async Task<FbxConversionResult> ConvertAsync(FbxImportRequest request, CancellationToken cancellationToken = default)
    {
        StringBuilder log = new();

        if (!Directory.Exists(request.SourceFolder))
            return Fail($"Source folder does not exist: {request.SourceFolder}");

        string? blender = ResolveBlenderExe(request.BlenderExePath);
        if (blender == null)
            return Fail("Could not find Blender. Browse to blender.exe or set HS2_BLENDER_EXE.");

        string? fbxPath = Directory
            .EnumerateFiles(request.SourceFolder, "*.fbx", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (fbxPath == null)
            return Fail("No .fbx file was found in the selected source folder.");

        Directory.CreateDirectory(request.DestinationFolder);
        string outputName = SanitizeFileName(request.OutputName);
        if (string.IsNullOrWhiteSpace(outputName))
            outputName = Path.GetFileNameWithoutExtension(fbxPath);

        string outputPath = Path.Combine(request.DestinationFolder, outputName + ".glb");
        string textureSearchRoot = ResolveTextureSearchRoot(request.SourceFolder, fbxPath);
        string scriptPath = Path.Combine(Path.GetTempPath(), $"hs2_fbx_to_glb_{Guid.NewGuid():N}.py");
        await File.WriteAllTextAsync(scriptPath, BlenderExportScript, cancellationToken);

        try
        {
            log.AppendLine($"Blender: {blender}");
            log.AppendLine($"FBX: {fbxPath}");
            log.AppendLine($"Texture search root: {textureSearchRoot}");
            log.AppendLine($"Output: {outputPath}");

            using Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = blender,
                WorkingDirectory = request.SourceFolder,
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

            process.Start();
            string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(stdout))
                log.AppendLine(stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(stderr))
                log.AppendLine(stderr.TrimEnd());

            if (process.ExitCode != 0)
                return new FbxConversionResult(false, null, log.AppendLine($"Blender exited with code {process.ExitCode}.").ToString());

            if (!File.Exists(outputPath))
                return new FbxConversionResult(false, null, log.AppendLine("Blender finished but no GLB output was produced.").ToString());

            return new FbxConversionResult(true, outputPath, log.AppendLine("Conversion complete.").ToString());
        }
        catch (Exception ex)
        {
            log.AppendLine(ex.ToString());
            return new FbxConversionResult(false, null, log.ToString());
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

    private static string SanitizeFileName(string value)
    {
        string name = Path.GetFileNameWithoutExtension(value.Trim());
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return name;
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
import sys
import traceback

args = sys.argv[sys.argv.index("--") + 1:]
fbx_path = args[0]
output_path = args[1]
source_root = args[2]
max_texture_size = 1024

def material_match_score(file_name, material_hint):
    if not material_hint:
        return 0

    stem = os.path.splitext(file_name)[0].lower().replace("-", "_").replace(" ", "_")
    hint = material_hint.lower().replace("-", "_").replace(" ", "_")
    tokens = [token for token in stem.split("_") if token]

    if stem == hint:
        return 120
    if stem.startswith(hint + "_"):
        return 100
    if hint in tokens:
        return 60
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
    hint = material_name.lower().replace(" ", "_") if material_name else None
    albedo = find_texture(("albedo", "basecolor", "base_color", "diffuse", "_color", "color"), hint, ("emission", "emissive", "normal", "metallic", "metalness", "rough", "opacity"))
    normal = find_texture(("normal", "norm"), hint)
    emission = find_texture(("emission", "emissive"), hint, allow_generic=False)
    metallic = find_texture(("metallic", "metalness"), hint)
    roughness = find_texture(("roughness", "rough"), hint)
    return albedo, normal, emission, metallic, roughness

for mat in bpy.data.materials:
    albedo, normal, emission, metallic, roughness = material_textures(mat.name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    shader = nodes.get("Principled BSDF")
    if shader is None:
        continue
    attach_texture(nodes, links, shader, "Base Color", albedo, "sRGB")
    attach_normal(nodes, links, shader, normal)
    attach_texture(nodes, links, shader, "Emission Color", emission, "sRGB")
    attach_texture(nodes, links, shader, "Metallic", metallic, "Non-Color")
    attach_texture(nodes, links, shader, "Roughness", roughness, "Non-Color")

if len(bpy.data.materials) == 0:
    mat = bpy.data.materials.new(name="Imported_Weapon_Material")
    albedo, normal, emission, metallic, roughness = material_textures(mat.name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    shader = nodes.get("Principled BSDF")
    if shader is not None:
        attach_texture(nodes, links, shader, "Base Color", albedo, "sRGB")
        attach_normal(nodes, links, shader, normal)
        attach_texture(nodes, links, shader, "Emission Color", emission, "sRGB")
        attach_texture(nodes, links, shader, "Metallic", metallic, "Non-Color")
        attach_texture(nodes, links, shader, "Roughness", roughness, "Non-Color")
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
}
