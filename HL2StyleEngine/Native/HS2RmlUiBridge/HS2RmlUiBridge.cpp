#include "HS2RmlUiBridge.h"

#include <RmlUi/Core.h>
#include <RmlUi/Core/Context.h>
#include <RmlUi/Core/ElementDocument.h>
#include <RmlUi/Core/FileInterface.h>
#include <RmlUi/Core/FontEngineInterface.h>
#include <RmlUi/Core/RenderInterface.h>
#include <RmlUi/Core/SystemInterface.h>
#include <RmlUi/Core/Vertex.h>
#include <lodepng.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cstdio>
#include <cctype>
#include <filesystem>
#include <memory>
#include <string>
#include <system_error>
#include <unordered_map>
#include <vector>

#if defined(_WIN32)
#include <Windows.h>
#endif

namespace
{
struct BridgeGeometry
{
    std::vector<hs2_rmlui_vertex> vertices;
    std::vector<uint32_t> indices;
};

struct BridgeTexture
{
    uint64_t id = 0;
    int32_t width = 0;
    int32_t height = 0;
    std::vector<uint8_t> rgba;
    bool dirty = true;
};

struct BridgeRenderCommand
{
    const BridgeGeometry* geometry = nullptr;
    uint64_t textureId = 0;
    bool scissorEnabled = false;
    int32_t scissorX = 0;
    int32_t scissorY = 0;
    int32_t scissorWidth = 0;
    int32_t scissorHeight = 0;
    float translateX = 0.0f;
    float translateY = 0.0f;
};

struct BridgeDocument;

static bool g_rmlInitialised = false;
static int g_contextCount = 0;

static std::string ToString(const char* text)
{
    return text != nullptr ? std::string(text) : std::string();
}

static std::string NormalizeSlashes(std::string value)
{
    std::replace(value.begin(), value.end(), '\\', '/');
    return value;
}

static bool IsAbsolutePath(const std::string& path)
{
    if (path.size() >= 2 && path[1] == ':')
        return true;

    return std::filesystem::path(path).is_absolute();
}

static std::string ResolvePath(const std::string& root, const std::string& path)
{
    if (path.empty())
        return root;

    if (IsAbsolutePath(path))
    {
        std::error_code error;
        std::filesystem::path resolved = std::filesystem::weakly_canonical(std::filesystem::path(path), error);
        if (error)
            resolved = std::filesystem::path(path).lexically_normal();
        return NormalizeSlashes(resolved.string());
    }

    std::filesystem::path resolved = std::filesystem::path(root) / std::filesystem::path(path);
    std::error_code error;
    std::filesystem::path canonical = std::filesystem::weakly_canonical(resolved, error);
    if (error)
        canonical = resolved.lexically_normal();
    return NormalizeSlashes(canonical.string());
}

class BridgeSystemInterface final : public Rml::SystemInterface
{
public:
    BridgeSystemInterface()
        : _start(std::chrono::steady_clock::now())
    {
    }

    double GetElapsedTime() override
    {
        auto now = std::chrono::steady_clock::now();
        return std::chrono::duration<double>(now - _start).count();
    }

    void JoinPath(Rml::String& translatedPath, const Rml::String& documentPath, const Rml::String& path) override
    {
        if (IsAbsolutePath(path))
        {
            translatedPath = NormalizeSlashes(path);
            return;
        }

        std::filesystem::path parent = std::filesystem::path(documentPath).parent_path();
        translatedPath = NormalizeSlashes((parent / std::filesystem::path(path)).generic_string());
    }

    bool LogMessage(Rml::Log::Type, const Rml::String& message) override
    {
#if defined(_WIN32)
        std::string line = "[HS2RmlUiBridge] " + message + "\n";
        OutputDebugStringA(line.c_str());
#else
        (void)message;
#endif
        return true;
    }

private:
    std::chrono::steady_clock::time_point _start;
};

class BridgeFileInterface final : public Rml::FileInterface
{
public:
    explicit BridgeFileInterface(std::string root)
        : _root(std::move(root))
    {
    }

    Rml::FileHandle Open(const Rml::String& path) override
    {
        std::string resolved = ResolvePath(_root, path);
        std::FILE* file = nullptr;
        fopen_s(&file, resolved.c_str(), "rb");
        return reinterpret_cast<Rml::FileHandle>(file);
    }

    void Close(Rml::FileHandle file) override
    {
        if (file != 0)
            std::fclose(reinterpret_cast<std::FILE*>(file));
    }

    size_t Read(void* buffer, size_t size, Rml::FileHandle file) override
    {
        if (file == 0 || buffer == nullptr || size == 0)
            return 0;

        return std::fread(buffer, 1, size, reinterpret_cast<std::FILE*>(file));
    }

    bool Seek(Rml::FileHandle file, long offset, int origin) override
    {
        if (file == 0)
            return false;

        return std::fseek(reinterpret_cast<std::FILE*>(file), offset, origin) == 0;
    }

    size_t Tell(Rml::FileHandle file) override
    {
        if (file == 0)
            return 0;

        long position = std::ftell(reinterpret_cast<std::FILE*>(file));
        return position < 0 ? 0u : static_cast<size_t>(position);
    }

private:
    std::string _root;
};

class BridgeFontInterface final : public Rml::FontEngineInterface
{
public:
    BridgeFontInterface()
    {
        _metrics.size = 16;
        _metrics.ascent = 12.0f;
        _metrics.descent = 4.0f;
        _metrics.line_spacing = 18.0f;
        _metrics.x_height = 8.0f;
        _metrics.underline_position = 2.0f;
        _metrics.underline_thickness = 1.0f;
        _metrics.has_ellipsis = false;
    }

    bool LoadFontFace(const Rml::String&, int, bool, Rml::Style::FontWeight) override { return true; }

    bool LoadFontFace(
        const Rml::String&,
        int,
        const Rml::String&,
        Rml::Style::FontStyle,
        Rml::Style::FontWeight,
        bool) override
    {
        return true;
    }

    bool LoadFontFace(
        Rml::Span<const Rml::byte>,
        int,
        const Rml::String&,
        Rml::Style::FontStyle,
        Rml::Style::FontWeight,
        bool) override
    {
        return true;
    }

    Rml::FontFaceHandle GetFontFaceHandle(const Rml::String&, Rml::Style::FontStyle, Rml::Style::FontWeight, int size) override
    {
        _metrics.size = std::max(1, size);
        _metrics.ascent = _metrics.size * 0.75f;
        _metrics.descent = _metrics.size * 0.25f;
        _metrics.line_spacing = _metrics.size * 1.2f;
        _metrics.x_height = _metrics.size * 0.5f;
        return 1;
    }

    Rml::FontEffectsHandle PrepareFontEffects(Rml::FontFaceHandle, const Rml::FontEffectList&) override
    {
        return 1;
    }

    const Rml::FontMetrics& GetFontMetrics(Rml::FontFaceHandle) override
    {
        return _metrics;
    }

    int GetStringWidth(
        Rml::FontFaceHandle,
        Rml::StringView string,
        const Rml::TextShapingContext&,
        Rml::Character = Rml::Character::Null) override
    {
        return Measure(string);
    }

    int GenerateString(
        Rml::RenderManager&,
        Rml::FontFaceHandle,
        Rml::FontEffectsHandle,
        Rml::StringView string,
        Rml::Vector2f position,
        Rml::ColourbPremultiplied colour,
        float opacity,
        const Rml::TextShapingContext&,
        Rml::TexturedMeshList& mesh_list) override
    {
        Rml::TexturedMesh textMesh;
        Rml::Mesh& mesh = textMesh.mesh;
        float cell = CellSize();
        float cursorX = position.x;
        float topY = position.y - _metrics.ascent;
        Rml::ColourbPremultiplied finalColour = colour;
        finalColour.alpha = static_cast<Rml::byte>(std::clamp(opacity, 0.0f, 1.0f) * finalColour.alpha);

        for (char raw : string)
        {
            if (raw == ' ')
            {
                cursorX += cell * 4.0f;
                continue;
            }

            std::array<const char*, 7> pattern = Glyph(raw);
            for (int row = 0; row < 7; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    if (pattern[row][col] != '1')
                        continue;

                    AddQuad(mesh, cursorX + (col * cell), topY + (row * cell), cell, cell, finalColour);
                }
            }

            cursorX += cell * 6.0f;
        }

        if (mesh)
            mesh_list.push_back(std::move(textMesh));

        return Measure(string);
    }

    int GetVersion(Rml::FontFaceHandle) override { return 1; }

    void ReleaseFontResources() override {}

private:
    float CellSize() const
    {
        return std::max(1.0f, _metrics.size / 8.0f);
    }

    int Measure(Rml::StringView string) const
    {
        float cell = CellSize();
        float width = 0.0f;
        for (char c : string)
            width += c == ' ' ? cell * 4.0f : cell * 6.0f;

        return static_cast<int>(width);
    }

    static void AddQuad(Rml::Mesh& mesh, float x, float y, float width, float height, Rml::ColourbPremultiplied colour)
    {
        int start = static_cast<int>(mesh.vertices.size());
        mesh.vertices.push_back({ Rml::Vector2f(x, y), colour, Rml::Vector2f(0.0f, 0.0f) });
        mesh.vertices.push_back({ Rml::Vector2f(x + width, y), colour, Rml::Vector2f(0.0f, 0.0f) });
        mesh.vertices.push_back({ Rml::Vector2f(x + width, y + height), colour, Rml::Vector2f(0.0f, 0.0f) });
        mesh.vertices.push_back({ Rml::Vector2f(x, y + height), colour, Rml::Vector2f(0.0f, 0.0f) });
        mesh.indices.push_back(start + 0);
        mesh.indices.push_back(start + 1);
        mesh.indices.push_back(start + 2);
        mesh.indices.push_back(start + 0);
        mesh.indices.push_back(start + 2);
        mesh.indices.push_back(start + 3);
    }

    static std::array<const char*, 7> Glyph(char raw)
    {
        char c = static_cast<char>(std::toupper(static_cast<unsigned char>(raw)));
        switch (c)
        {
        case 'A': return { "01110", "10001", "10001", "11111", "10001", "10001", "10001" };
        case 'B': return { "11110", "10001", "10001", "11110", "10001", "10001", "11110" };
        case 'C': return { "01111", "10000", "10000", "10000", "10000", "10000", "01111" };
        case 'D': return { "11110", "10001", "10001", "10001", "10001", "10001", "11110" };
        case 'E': return { "11111", "10000", "10000", "11110", "10000", "10000", "11111" };
        case 'F': return { "11111", "10000", "10000", "11110", "10000", "10000", "10000" };
        case 'G': return { "01111", "10000", "10000", "10011", "10001", "10001", "01111" };
        case 'H': return { "10001", "10001", "10001", "11111", "10001", "10001", "10001" };
        case 'I': return { "11111", "00100", "00100", "00100", "00100", "00100", "11111" };
        case 'J': return { "00111", "00010", "00010", "00010", "10010", "10010", "01100" };
        case 'K': return { "10001", "10010", "10100", "11000", "10100", "10010", "10001" };
        case 'L': return { "10000", "10000", "10000", "10000", "10000", "10000", "11111" };
        case 'M': return { "10001", "11011", "10101", "10101", "10001", "10001", "10001" };
        case 'N': return { "10001", "11001", "10101", "10011", "10001", "10001", "10001" };
        case 'O': return { "01110", "10001", "10001", "10001", "10001", "10001", "01110" };
        case 'P': return { "11110", "10001", "10001", "11110", "10000", "10000", "10000" };
        case 'Q': return { "01110", "10001", "10001", "10001", "10101", "10010", "01101" };
        case 'R': return { "11110", "10001", "10001", "11110", "10100", "10010", "10001" };
        case 'S': return { "01111", "10000", "10000", "01110", "00001", "00001", "11110" };
        case 'T': return { "11111", "00100", "00100", "00100", "00100", "00100", "00100" };
        case 'U': return { "10001", "10001", "10001", "10001", "10001", "10001", "01110" };
        case 'V': return { "10001", "10001", "10001", "10001", "10001", "01010", "00100" };
        case 'W': return { "10001", "10001", "10001", "10101", "10101", "10101", "01010" };
        case 'X': return { "10001", "10001", "01010", "00100", "01010", "10001", "10001" };
        case 'Y': return { "10001", "10001", "01010", "00100", "00100", "00100", "00100" };
        case 'Z': return { "11111", "00001", "00010", "00100", "01000", "10000", "11111" };
        case '0': return { "01110", "10001", "10011", "10101", "11001", "10001", "01110" };
        case '1': return { "00100", "01100", "00100", "00100", "00100", "00100", "01110" };
        case '2': return { "01110", "10001", "00001", "00010", "00100", "01000", "11111" };
        case '3': return { "11110", "00001", "00001", "01110", "00001", "00001", "11110" };
        case '4': return { "10010", "10010", "10010", "11111", "00010", "00010", "00010" };
        case '5': return { "11111", "10000", "10000", "11110", "00001", "00001", "11110" };
        case '6': return { "01111", "10000", "10000", "11110", "10001", "10001", "01110" };
        case '7': return { "11111", "00001", "00010", "00100", "01000", "01000", "01000" };
        case '8': return { "01110", "10001", "10001", "01110", "10001", "10001", "01110" };
        case '9': return { "01110", "10001", "10001", "01111", "00001", "00001", "11110" };
        case ':': return { "00000", "00100", "00100", "00000", "00100", "00100", "00000" };
        case '.': return { "00000", "00000", "00000", "00000", "00000", "01100", "01100" };
        case ',': return { "00000", "00000", "00000", "00000", "00100", "00100", "01000" };
        case '-': return { "00000", "00000", "00000", "11111", "00000", "00000", "00000" };
        case '/': return { "00001", "00010", "00010", "00100", "01000", "01000", "10000" };
        case '?': return { "01110", "10001", "00001", "00010", "00100", "00000", "00100" };
        case '!': return { "00100", "00100", "00100", "00100", "00100", "00000", "00100" };
        default: return { "11111", "10001", "10101", "10101", "10101", "10001", "11111" };
        }
    }

    Rml::FontMetrics _metrics = {};
};

class BridgeRenderInterface final : public Rml::RenderInterface
{
public:
    explicit BridgeRenderInterface(std::string contentRoot)
        : _contentRoot(std::move(contentRoot))
    {
    }

    void BeginFrame(int32_t viewportWidth, int32_t viewportHeight)
    {
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        _renderCommands.clear();
        _exportCommands.clear();
        _exportRenderData = {};
    }

    Rml::CompiledGeometryHandle CompileGeometry(Rml::Span<const Rml::Vertex> vertices, Rml::Span<const int> indices) override
    {
        auto geometry = std::make_unique<BridgeGeometry>();
        geometry->vertices.reserve(vertices.size());
        geometry->indices.reserve(indices.size());

        for (const Rml::Vertex& vertex : vertices)
        {
            uint32_t color =
                static_cast<uint32_t>(vertex.colour.red) |
                (static_cast<uint32_t>(vertex.colour.green) << 8) |
                (static_cast<uint32_t>(vertex.colour.blue) << 16) |
                (static_cast<uint32_t>(vertex.colour.alpha) << 24);

            geometry->vertices.push_back({
                vertex.position.x,
                vertex.position.y,
                vertex.tex_coord.x,
                vertex.tex_coord.y,
                color });
        }

        for (int index : indices)
            geometry->indices.push_back(static_cast<uint32_t>(index));

        return reinterpret_cast<Rml::CompiledGeometryHandle>(geometry.release());
    }

    void RenderGeometry(Rml::CompiledGeometryHandle geometryHandle, Rml::Vector2f translation, Rml::TextureHandle texture) override
    {
        const BridgeGeometry* geometry = reinterpret_cast<const BridgeGeometry*>(geometryHandle);
        if (geometry == nullptr)
            return;

        BridgeRenderCommand command;
        command.geometry = geometry;
        command.textureId = static_cast<uint64_t>(texture);
        command.scissorEnabled = _scissorEnabled;
        command.scissorX = _scissorX;
        command.scissorY = _scissorY;
        command.scissorWidth = _scissorWidth;
        command.scissorHeight = _scissorHeight;
        command.translateX = translation.x;
        command.translateY = translation.y;
        _renderCommands.push_back(command);
    }

    void ReleaseGeometry(Rml::CompiledGeometryHandle geometry) override
    {
        delete reinterpret_cast<BridgeGeometry*>(geometry);
    }

    Rml::TextureHandle LoadTexture(Rml::Vector2i& textureDimensions, const Rml::String& source) override
    {
        std::vector<unsigned char> image;
        unsigned width = 0;
        unsigned height = 0;
        std::string resolved = ResolvePath(_contentRoot, source);
        unsigned error = lodepng::decode(image, width, height, resolved);
        if (error != 0 || image.empty() || width == 0 || height == 0)
            return 0;

        PremultiplyAlpha(image);
        textureDimensions = Rml::Vector2i(static_cast<int>(width), static_cast<int>(height));
        return AddTexture(static_cast<int32_t>(width), static_cast<int32_t>(height), image);
    }

    Rml::TextureHandle GenerateTexture(Rml::Span<const Rml::byte> source, Rml::Vector2i sourceDimensions) override
    {
        if (source.empty() || sourceDimensions.x <= 0 || sourceDimensions.y <= 0)
            return 0;

        std::vector<uint8_t> rgba(source.begin(), source.end());
        return AddTexture(sourceDimensions.x, sourceDimensions.y, rgba);
    }

    void ReleaseTexture(Rml::TextureHandle texture) override
    {
        if (texture != 0)
            _textures.erase(static_cast<uint64_t>(texture));
    }

    void EnableScissorRegion(bool enable) override
    {
        _scissorEnabled = enable;
    }

    void SetScissorRegion(Rml::Rectanglei region) override
    {
        _scissorX = region.Left();
        _scissorY = region.Top();
        _scissorWidth = region.Width();
        _scissorHeight = region.Height();
    }

    const hs2_rmlui_render_data* BuildRenderData()
    {
        _exportCommands.clear();
        _exportCommands.reserve(_renderCommands.size());

        for (const BridgeRenderCommand& source : _renderCommands)
        {
            if (source.geometry == nullptr)
                continue;

            hs2_rmlui_render_command command = {};
            command.vertices = source.geometry->vertices.data();
            command.vertex_count = static_cast<int32_t>(source.geometry->vertices.size());
            command.indices = source.geometry->indices.data();
            command.index_count = static_cast<int32_t>(source.geometry->indices.size());
            command.texture_id = source.textureId;
            command.scissor_enabled = source.scissorEnabled ? 1 : 0;
            command.scissor_x = source.scissorX;
            command.scissor_y = source.scissorY;
            command.scissor_width = source.scissorWidth;
            command.scissor_height = source.scissorHeight;
            command.translate_x = source.translateX;
            command.translate_y = source.translateY;
            _exportCommands.push_back(command);
        }

        _exportRenderData.commands = _exportCommands.data();
        _exportRenderData.command_count = static_cast<int32_t>(_exportCommands.size());
        _exportRenderData.viewport_width = _viewportWidth;
        _exportRenderData.viewport_height = _viewportHeight;
        return &_exportRenderData;
    }

    const hs2_rmlui_texture_data* BuildTextureData(int32_t& count)
    {
        _exportTextures.clear();

        for (auto& pair : _textures)
        {
            BridgeTexture& texture = pair.second;
            if (!texture.dirty || texture.rgba.empty())
                continue;

            hs2_rmlui_texture_data data = {};
            data.texture_id = texture.id;
            data.rgba = texture.rgba.data();
            data.width = texture.width;
            data.height = texture.height;
            data.byte_count = static_cast<int32_t>(texture.rgba.size());
            _exportTextures.push_back(data);
            texture.dirty = false;
        }

        count = static_cast<int32_t>(_exportTextures.size());
        return _exportTextures.data();
    }

private:
    static void PremultiplyAlpha(std::vector<unsigned char>& rgba)
    {
        for (size_t i = 0; i + 3 < rgba.size(); i += 4)
        {
            unsigned alpha = rgba[i + 3];
            rgba[i + 0] = static_cast<unsigned char>((static_cast<unsigned>(rgba[i + 0]) * alpha) / 255u);
            rgba[i + 1] = static_cast<unsigned char>((static_cast<unsigned>(rgba[i + 1]) * alpha) / 255u);
            rgba[i + 2] = static_cast<unsigned char>((static_cast<unsigned>(rgba[i + 2]) * alpha) / 255u);
        }
    }

    Rml::TextureHandle AddTexture(int32_t width, int32_t height, std::vector<uint8_t>& rgba)
    {
        uint64_t id = _nextTextureId++;
        BridgeTexture texture;
        texture.id = id;
        texture.width = width;
        texture.height = height;
        texture.rgba.swap(rgba);
        texture.dirty = true;
        _textures[id] = std::move(texture);
        return static_cast<Rml::TextureHandle>(id);
    }

    std::string _contentRoot;
    int32_t _viewportWidth = 1;
    int32_t _viewportHeight = 1;
    bool _scissorEnabled = false;
    int32_t _scissorX = 0;
    int32_t _scissorY = 0;
    int32_t _scissorWidth = 0;
    int32_t _scissorHeight = 0;
    uint64_t _nextTextureId = 1;
    std::vector<BridgeRenderCommand> _renderCommands;
    std::vector<hs2_rmlui_render_command> _exportCommands;
    std::vector<hs2_rmlui_texture_data> _exportTextures;
    hs2_rmlui_render_data _exportRenderData = {};
    std::unordered_map<uint64_t, BridgeTexture> _textures;
};

struct BridgeContext
{
    BridgeContext(std::string root, int32_t width, int32_t height)
        : contentRoot(std::move(root)),
          system(),
          file(contentRoot),
          font(),
          renderer(contentRoot),
          viewportWidth(width),
          viewportHeight(height)
    {
    }

    std::string contentRoot;
    BridgeSystemInterface system;
    BridgeFileInterface file;
    BridgeFontInterface font;
    BridgeRenderInterface renderer;
    Rml::Context* context = nullptr;
    int32_t viewportWidth = 1;
    int32_t viewportHeight = 1;
    bool mouseButtons[8] = {};
    bool keys[512] = {};
    std::vector<std::unique_ptr<BridgeDocument>> documents;
};

struct BridgeDocument
{
    BridgeContext* owner = nullptr;
    Rml::ElementDocument* document = nullptr;
    std::string sourceUrl;
    bool visible = false;
};

static BridgeContext* AsContext(hs2_rmlui_context context)
{
    return reinterpret_cast<BridgeContext*>(context);
}

static BridgeDocument* AsDocument(hs2_rmlui_document document)
{
    return reinterpret_cast<BridgeDocument*>(document);
}

static bool TryGetDataSlotFromElement(Rml::Element* element, int32_t* outSlot)
{
    if (outSlot == nullptr)
        return false;

    for (Rml::Element* current = element; current != nullptr; current = current->GetParentNode())
    {
        const Rml::Variant* dataSlot = current->GetAttribute("data-slot");
        if (dataSlot == nullptr)
            continue;

        int slot = dataSlot->Get<int>(-1);
        if (slot >= 0)
        {
            *outSlot = slot;
            return true;
        }
    }

    return false;
}
} // namespace

extern "C"
{
HS2_RMLUI_API int32_t hs2_rmlui_create_context(
    const char* content_root_utf8,
    int32_t width,
    int32_t height,
    hs2_rmlui_context* out_context)
{
    if (out_context == nullptr)
        return 0;

    *out_context = nullptr;
    std::string root = ResolvePath("", ToString(content_root_utf8));
    auto bridge = std::make_unique<BridgeContext>(root, std::max(1, width), std::max(1, height));

    if (!g_rmlInitialised)
    {
        Rml::SetSystemInterface(&bridge->system);
        Rml::SetFileInterface(&bridge->file);
        Rml::SetFontEngineInterface(&bridge->font);
        Rml::SetRenderInterface(&bridge->renderer);

        if (!Rml::Initialise())
            return 0;

        g_rmlInitialised = true;
    }

    std::string contextName = "HS2RmlUi_" + std::to_string(g_contextCount + 1);
    bridge->context = Rml::CreateContext(
        contextName,
        Rml::Vector2i(bridge->viewportWidth, bridge->viewportHeight),
        &bridge->renderer);

    if (bridge->context == nullptr)
        return 0;

    bridge->context->EnableMouseCursor(true);
    *out_context = bridge.release();
    ++g_contextCount;
    return 1;
}

HS2_RMLUI_API void hs2_rmlui_destroy_context(hs2_rmlui_context context)
{
    BridgeContext* bridge = AsContext(context);
    if (bridge == nullptr)
        return;

    if (bridge->context != nullptr)
    {
        for (std::unique_ptr<BridgeDocument>& wrapper : bridge->documents)
        {
            if (wrapper && wrapper->document != nullptr)
            {
                wrapper->document->Hide();
                bridge->context->UnloadDocument(wrapper->document);
                wrapper->document = nullptr;
            }
        }

        std::string name = bridge->context->GetName();
        Rml::RemoveContext(name);
        bridge->context = nullptr;
    }

    if (g_contextCount > 0)
        --g_contextCount;

    if (g_contextCount == 0 && g_rmlInitialised)
    {
        Rml::Shutdown();
        g_rmlInitialised = false;
    }

    delete bridge;
}

HS2_RMLUI_API void hs2_rmlui_set_viewport(hs2_rmlui_context context, int32_t width, int32_t height)
{
    BridgeContext* bridge = AsContext(context);
    if (bridge == nullptr || bridge->context == nullptr)
        return;

    bridge->viewportWidth = std::max(1, width);
    bridge->viewportHeight = std::max(1, height);
    bridge->context->SetDimensions(Rml::Vector2i(bridge->viewportWidth, bridge->viewportHeight));
}

HS2_RMLUI_API int32_t hs2_rmlui_load_document(
    hs2_rmlui_context context,
    const char* document_path_utf8,
    hs2_rmlui_document* out_document)
{
    BridgeContext* bridge = AsContext(context);
    if (bridge == nullptr || bridge->context == nullptr || out_document == nullptr)
        return 0;

    *out_document = nullptr;
    std::string documentPath = NormalizeSlashes(ToString(document_path_utf8));
    Rml::ElementDocument* document = bridge->context->LoadDocument(documentPath);
    if (document == nullptr)
        return 0;

    auto wrapper = std::make_unique<BridgeDocument>();
    wrapper->owner = bridge;
    wrapper->document = document;
    wrapper->sourceUrl = documentPath;

    BridgeDocument* wrapperPtr = wrapper.get();
    bridge->documents.push_back(std::move(wrapper));
    *out_document = wrapperPtr;
    return 1;
}

HS2_RMLUI_API void hs2_rmlui_show_document(hs2_rmlui_document document)
{
    BridgeDocument* wrapper = AsDocument(document);
    if (wrapper == nullptr || wrapper->document == nullptr)
        return;

    wrapper->document->Show();
    wrapper->visible = true;
}

HS2_RMLUI_API void hs2_rmlui_hide_document(hs2_rmlui_document document)
{
    BridgeDocument* wrapper = AsDocument(document);
    if (wrapper == nullptr || wrapper->document == nullptr)
        return;

    wrapper->document->Hide();
    wrapper->visible = false;
}

HS2_RMLUI_API void hs2_rmlui_update(hs2_rmlui_context context, float)
{
    BridgeContext* bridge = AsContext(context);
    if (bridge == nullptr || bridge->context == nullptr)
        return;

    bridge->context->Update();
}

HS2_RMLUI_API void hs2_rmlui_render(hs2_rmlui_context context)
{
    BridgeContext* bridge = AsContext(context);
    if (bridge == nullptr || bridge->context == nullptr)
        return;

    bridge->renderer.BeginFrame(bridge->viewportWidth, bridge->viewportHeight);
    bridge->context->Render();
}

HS2_RMLUI_API void hs2_rmlui_set_mouse_position(hs2_rmlui_context context, float x, float y)
{
    BridgeContext* bridge = AsContext(context);
    if (bridge == nullptr || bridge->context == nullptr)
        return;

    bridge->context->ProcessMouseMove(static_cast<int>(x), static_cast<int>(y), 0);
}

HS2_RMLUI_API void hs2_rmlui_set_mouse_button(hs2_rmlui_context context, int32_t button, int32_t down)
{
    BridgeContext* bridge = AsContext(context);
    if (bridge == nullptr || bridge->context == nullptr || button < 0 || button >= 8)
        return;

    bool isDown = down != 0;
    if (bridge->mouseButtons[button] == isDown)
        return;

    bridge->mouseButtons[button] = isDown;
    if (isDown)
        bridge->context->ProcessMouseButtonDown(button, 0);
    else
        bridge->context->ProcessMouseButtonUp(button, 0);
}

HS2_RMLUI_API void hs2_rmlui_set_key(hs2_rmlui_context context, int32_t key, int32_t down)
{
    BridgeContext* bridge = AsContext(context);
    if (bridge == nullptr || bridge->context == nullptr || key < 0 || key >= 512)
        return;

    bool isDown = down != 0;
    if (bridge->keys[key] == isDown)
        return;

    bridge->keys[key] = isDown;
    Rml::Input::KeyIdentifier identifier = static_cast<Rml::Input::KeyIdentifier>(key);
    if (isDown)
        bridge->context->ProcessKeyDown(identifier, 0);
    else
        bridge->context->ProcessKeyUp(identifier, 0);
}

HS2_RMLUI_API void hs2_rmlui_submit_text(hs2_rmlui_context context, const char* text_utf8)
{
    BridgeContext* bridge = AsContext(context);
    if (bridge == nullptr || bridge->context == nullptr || text_utf8 == nullptr)
        return;

    bridge->context->ProcessTextInput(ToString(text_utf8));
}

HS2_RMLUI_API int32_t hs2_rmlui_get_hovered_data_slot(hs2_rmlui_context context, int32_t* out_slot)
{
    if (out_slot == nullptr)
        return 0;

    *out_slot = -1;
    BridgeContext* bridge = AsContext(context);
    if (bridge == nullptr || bridge->context == nullptr)
        return 0;

    Rml::Element* hovered = bridge->context->GetHoverElement();
    return TryGetDataSlotFromElement(hovered, out_slot) ? 1 : 0;
}

HS2_RMLUI_API int32_t hs2_rmlui_get_render_data(hs2_rmlui_context context, hs2_rmlui_render_data* out_render_data)
{
    BridgeContext* bridge = AsContext(context);
    if (bridge == nullptr || out_render_data == nullptr)
        return 0;

    const hs2_rmlui_render_data* data = bridge->renderer.BuildRenderData();
    *out_render_data = *data;
    return 1;
}

HS2_RMLUI_API void hs2_rmlui_release_render_data(hs2_rmlui_context)
{
}

HS2_RMLUI_API int32_t hs2_rmlui_get_texture_data(
    hs2_rmlui_context context,
    const hs2_rmlui_texture_data** out_textures,
    int32_t* out_count)
{
    BridgeContext* bridge = AsContext(context);
    if (bridge == nullptr || out_textures == nullptr || out_count == nullptr)
        return 0;

    int32_t count = 0;
    const hs2_rmlui_texture_data* textures = bridge->renderer.BuildTextureData(count);
    *out_textures = textures;
    *out_count = count;
    return 1;
}

HS2_RMLUI_API void hs2_rmlui_release_texture_data(hs2_rmlui_context)
{
}

HS2_RMLUI_API int32_t hs2_rmlui_set_document_body(hs2_rmlui_document document, const char* body_rml_utf8)
{
    BridgeDocument* wrapper = AsDocument(document);
    if (wrapper == nullptr || wrapper->owner == nullptr || wrapper->owner->context == nullptr || body_rml_utf8 == nullptr)
        return 0;

    Rml::Context* context = wrapper->owner->context;
    if (wrapper->document != nullptr)
    {
        wrapper->document->Hide();
        context->UnloadDocument(wrapper->document);
        wrapper->document = nullptr;
    }

    Rml::ElementDocument* replacement = context->LoadDocumentFromMemory(ToString(body_rml_utf8), wrapper->sourceUrl);
    if (replacement == nullptr)
        return 0;

    wrapper->document = replacement;
    if (wrapper->visible)
        wrapper->document->Show();

    return 1;
}
}
