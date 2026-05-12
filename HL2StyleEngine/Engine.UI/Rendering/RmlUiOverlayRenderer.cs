using Engine.Render;
using Engine.UI.Native;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Engine.UI.Rendering;

internal sealed class RmlUiOverlayRenderer : IDisposable
{
    private const uint InitialVertexCapacity = 4096;
    private const uint InitialIndexCapacity = 8192;
    private const int MaxCommandsPerFrame = 512;
    private const int MaxVerticesPerFrame = 65536;
    private const int MaxIndicesPerFrame = 131072;

    private DeviceBuffer? _vertexBuffer;
    private DeviceBuffer? _indexBuffer;
    private ResourceLayout? _textureLayout;
    private ResourceSet? _fallbackTextureSet;
    private Sampler? _sampler;
    private Texture? _fallbackTexture;
    private TextureView? _fallbackTextureView;
    private readonly Dictionary<ulong, ResourceSet> _textureSets = new();
    private readonly Dictionary<ulong, Texture> _ownedTextures = new();
    private readonly Dictionary<ulong, TextureView> _ownedTextureViews = new();
    private Pipeline? _pipeline;
    private Shader[]? _shaders;
    private uint _vertexCapacity;
    private uint _indexCapacity;

    public string Status { get; private set; } = "RmlUi overlay renderer waiting for native render data.";
    public int LastSubmittedCommands { get; private set; }
    public int LastSubmittedTriangles { get; private set; }
    public int LastFallbackTextureCommands { get; private set; }
    public int LastTexturedCommands { get; private set; }
    public int RegisteredTextureCount => _textureSets.Count(set => set.Key != 0);
    public bool LastRenderSubmitted => LastSubmittedCommands > 0;

    public void Render(Renderer renderer, RmlUiRenderData renderData)
    {
        LastSubmittedCommands = 0;
        LastSubmittedTriangles = 0;
        LastFallbackTextureCommands = 0;
        LastTexturedCommands = 0;

        if (renderData.CommandCount <= 0 || renderData.Commands == IntPtr.Zero)
        {
            Status = "RmlUi produced no overlay draw commands this frame.";
            return;
        }

        if (!EnsureResources(renderer))
            return;

        if (renderData.CommandCount > MaxCommandsPerFrame)
        {
            Status = $"RmlUi frame rejected: {renderData.CommandCount} command(s) exceeds safety limit {MaxCommandsPerFrame}.";
            return;
        }

        int commandSize = Marshal.SizeOf<RmlUiRenderCommand>();

        int fallbackTextureCommands = 0;
        List<UiVertex> vertices = new();
        List<uint> indices = new();
        List<PreparedCommand> preparedCommands = new();

        for (int i = 0; i < renderData.CommandCount; i++)
        {
            IntPtr commandPtr = IntPtr.Add(renderData.Commands, i * commandSize);
            RmlUiRenderCommand command = Marshal.PtrToStructure<RmlUiRenderCommand>(commandPtr);
            if (command.VertexCount <= 0 ||
                command.IndexCount <= 0 ||
                command.Vertices == IntPtr.Zero ||
                command.Indices == IntPtr.Zero)
            {
                continue;
            }

            if (command.VertexCount > MaxVerticesPerFrame ||
                command.IndexCount > MaxIndicesPerFrame)
            {
                Status = $"RmlUi frame rejected: command {i} has unsafe geometry size ({command.VertexCount} vertices, {command.IndexCount} indices).";
                return;
            }

            if (vertices.Count + command.VertexCount > MaxVerticesPerFrame ||
                indices.Count + command.IndexCount > MaxIndicesPerFrame)
            {
                Status = $"RmlUi frame rejected: combined geometry exceeds safety limit ({vertices.Count + command.VertexCount} vertices, {indices.Count + command.IndexCount} indices).";
                return;
            }

            int vertexStart = vertices.Count;
            int indexStart = indices.Count;
            if (!TryBuildVertices(command, renderData, out UiVertex[] commandVertices) ||
                !TryBuildIndices(command, out uint[] commandIndices))
            {
                Status = $"RmlUi frame rejected: command {i} contained invalid vertex or index data.";
                return;
            }

            vertices.AddRange(commandVertices);
            indices.AddRange(commandIndices);
            preparedCommands.Add(new PreparedCommand(command, vertexStart, indexStart, command.IndexCount));
        }

        if (preparedCommands.Count == 0)
        {
            Status = $"RmlUi produced {renderData.CommandCount} command(s), but none had drawable geometry.";
            return;
        }

        EnsureBufferCapacity(renderer, (uint)vertices.Count, (uint)indices.Count);
        renderer.GraphicsDevice.UpdateBuffer(_vertexBuffer, 0, vertices.ToArray());
        renderer.GraphicsDevice.UpdateBuffer(_indexBuffer, 0, indices.ToArray());

        CommandList cl = renderer.CommandList;
        cl.SetPipeline(_pipeline);
        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);

        int submittedCommands = 0;
        int submittedTriangles = 0;

        foreach (PreparedCommand prepared in preparedCommands)
        {
            RmlUiRenderCommand command = prepared.Command;

            ResourceSet textureSet = GetTextureSet(command.TextureId, out bool usedFallbackTexture);
            if (command.TextureId != 0)
                LastTexturedCommands++;
            if (usedFallbackTexture)
                fallbackTextureCommands++;

            cl.SetGraphicsResourceSet(0, textureSet);
            ApplyScissor(cl, command, renderData);
            cl.DrawIndexed((uint)prepared.IndexCount, 1, (uint)prepared.IndexStart, prepared.VertexStart, 0);

            submittedCommands++;
            submittedTriangles += prepared.IndexCount / 3;
        }

        ResetScissor(cl, renderData);
        LastSubmittedCommands = submittedCommands;
        LastSubmittedTriangles = submittedTriangles;
        LastFallbackTextureCommands = fallbackTextureCommands;

        string fallbackNote = fallbackTextureCommands > 0
            ? $", {fallbackTextureCommands} command(s) used fallback texture"
            : "";
        string textureNote = LastTexturedCommands > 0
            ? $", {LastTexturedCommands} textured command(s), {RegisteredTextureCount} registered texture(s)"
            : "";
        Status = $"RmlUi submitted {submittedCommands}/{renderData.CommandCount} overlay command(s), {submittedTriangles} triangle(s){textureNote}{fallbackNote}.";
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _textureLayout?.Dispose();
        _fallbackTextureSet?.Dispose();
        _sampler?.Dispose();
        _fallbackTextureView?.Dispose();
        _fallbackTexture?.Dispose();
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();

        foreach (ResourceSet set in _textureSets.Values)
            set.Dispose();
        _textureSets.Clear();

        foreach (TextureView view in _ownedTextureViews.Values)
            view.Dispose();
        _ownedTextureViews.Clear();

        foreach (Texture texture in _ownedTextures.Values)
            texture.Dispose();
        _ownedTextures.Clear();

        if (_shaders != null)
        {
            foreach (Shader shader in _shaders)
                shader.Dispose();
        }
    }

    private bool EnsureResources(Renderer renderer)
    {
        if (_pipeline != null)
            return true;

        string shaderDir = Path.Combine(AppContext.BaseDirectory, "Shaders");
        string vsPath = Path.Combine(shaderDir, "RmlUiVS.cso");
        string psPath = Path.Combine(shaderDir, "RmlUiPS.cso");
        if (!File.Exists(vsPath) || !File.Exists(psPath))
        {
            Status = $"RmlUi overlay renderer could not find UI shaders in '{shaderDir}'.";
            return false;
        }

        ResourceFactory factory = renderer.Factory;
        GraphicsDevice graphicsDevice = renderer.GraphicsDevice;

        _vertexCapacity = InitialVertexCapacity;
        _indexCapacity = InitialIndexCapacity;
        _vertexBuffer = factory.CreateBuffer(new BufferDescription(
            _vertexCapacity * UiVertex.SizeInBytes,
            BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _indexBuffer = factory.CreateBuffer(new BufferDescription(
            _indexCapacity * sizeof(uint),
            BufferUsage.IndexBuffer | BufferUsage.Dynamic));

        _fallbackTexture = factory.CreateTexture(TextureDescription.Texture2D(
            1,
            1,
            1,
            1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled));
        graphicsDevice.UpdateTexture(
            _fallbackTexture,
            new byte[] { 255, 255, 255, 255 },
            0,
            0,
            0,
            1,
            1,
            1,
            0,
            0);
        _fallbackTextureView = factory.CreateTextureView(_fallbackTexture);
        _sampler = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipPoint,
            null,
            0,
            0,
            0,
            0,
            SamplerBorderColor.TransparentBlack));

        _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SourceTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SourceSamp", ResourceKind.Sampler, ShaderStages.Fragment)));
        _fallbackTextureSet = factory.CreateResourceSet(new ResourceSetDescription(
            _textureLayout,
            _fallbackTextureView,
            _sampler));

        _shaders = new[]
        {
            factory.CreateShader(new ShaderDescription(ShaderStages.Vertex, File.ReadAllBytes(vsPath), "VSMain")),
            factory.CreateShader(new ShaderDescription(ShaderStages.Fragment, File.ReadAllBytes(psPath), "PSMain"))
        };

        VertexLayoutDescription vertexLayout = new(
            new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float4));

        GraphicsPipelineDescription pipelineDescription = new()
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.None,
                PolygonFillMode.Solid,
                FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: true),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _textureLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, _shaders),
            Outputs = graphicsDevice.MainSwapchain.Framebuffer.OutputDescription
        };

        _pipeline = factory.CreateGraphicsPipeline(pipelineDescription);
        return true;
    }

    public void RegisterTexture(ulong textureId, Renderer renderer, TextureView textureView)
    {
        if (textureId == 0 || _textureLayout == null || _sampler == null)
            return;

        DisposeTextureRegistration(textureId);

        _textureSets[textureId] = renderer.Factory.CreateResourceSet(new ResourceSetDescription(
            _textureLayout,
            textureView,
            _sampler));
    }

    public void RegisterTextureRgba(ulong textureId, Renderer renderer, int width, int height, byte[] rgba)
    {
        if (textureId == 0 || width <= 0 || height <= 0 || rgba.Length < width * height * 4)
            return;

        if (!EnsureResources(renderer) || _textureLayout == null || _sampler == null)
            return;

        DisposeTextureRegistration(textureId);

        Texture texture = renderer.Factory.CreateTexture(TextureDescription.Texture2D(
            (uint)width,
            (uint)height,
            1,
            1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled));

        renderer.GraphicsDevice.UpdateTexture(
            texture,
            rgba,
            0,
            0,
            0,
            (uint)width,
            (uint)height,
            1,
            0,
            0);

        TextureView view = renderer.Factory.CreateTextureView(texture);
        ResourceSet set = renderer.Factory.CreateResourceSet(new ResourceSetDescription(
            _textureLayout,
            view,
            _sampler));

        _ownedTextures[textureId] = texture;
        _ownedTextureViews[textureId] = view;
        _textureSets[textureId] = set;
    }

    public void UnregisterTexture(ulong textureId)
    {
        DisposeTextureRegistration(textureId);
    }

    private void DisposeTextureRegistration(ulong textureId)
    {
        if (_textureSets.Remove(textureId, out ResourceSet? oldSet))
            oldSet.Dispose();

        if (_ownedTextureViews.Remove(textureId, out TextureView? oldView))
            oldView.Dispose();

        if (_ownedTextures.Remove(textureId, out Texture? oldTexture))
            oldTexture.Dispose();
    }

    private void EnsureBufferCapacity(Renderer renderer, uint vertexCount, uint indexCount)
    {
        ResourceFactory factory = renderer.Factory;

        if (vertexCount > _vertexCapacity)
        {
            _vertexCapacity = NextCapacity(vertexCount);
            _vertexBuffer?.Dispose();
            _vertexBuffer = factory.CreateBuffer(new BufferDescription(
                _vertexCapacity * UiVertex.SizeInBytes,
                BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        }

        if (indexCount > _indexCapacity)
        {
            _indexCapacity = NextCapacity(indexCount);
            _indexBuffer?.Dispose();
            _indexBuffer = factory.CreateBuffer(new BufferDescription(
                _indexCapacity * sizeof(uint),
                BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        }
    }

    private static uint NextCapacity(uint required)
    {
        uint capacity = 1;
        while (capacity < required)
            capacity <<= 1;

        return capacity;
    }

    private static bool TryBuildVertices(RmlUiRenderCommand command, RmlUiRenderData renderData, out UiVertex[] vertices)
    {
        vertices = new UiVertex[command.VertexCount];
        int vertexSize = Marshal.SizeOf<RmlUiVertex>();
        float width = Math.Max(1f, renderData.ViewportWidth);
        float height = Math.Max(1f, renderData.ViewportHeight);

        for (int i = 0; i < command.VertexCount; i++)
        {
            IntPtr vertexPtr = IntPtr.Add(command.Vertices, i * vertexSize);
            RmlUiVertex source = Marshal.PtrToStructure<RmlUiVertex>(vertexPtr);
            float x = source.X + command.TranslateX;
            float y = source.Y + command.TranslateY;
            Vector4 color = UnpackColor(source.ColorRgba);

            if (!float.IsFinite(x) ||
                !float.IsFinite(y) ||
                !float.IsFinite(source.U) ||
                !float.IsFinite(source.V) ||
                !Vector4IsFinite(color))
            {
                vertices = Array.Empty<UiVertex>();
                return false;
            }

            vertices[i] = new UiVertex(
                new Vector2((x / width * 2f) - 1f, 1f - (y / height * 2f)),
                new Vector2(source.U, source.V),
                color);
        }

        return true;
    }

    private ResourceSet GetTextureSet(ulong textureId, out bool usedFallbackTexture)
    {
        if (textureId != 0 && _textureSets.TryGetValue(textureId, out ResourceSet? textureSet))
        {
            usedFallbackTexture = false;
            return textureSet;
        }

        usedFallbackTexture = textureId != 0;
        return _fallbackTextureSet!;
    }

    private static Vector4 UnpackColor(uint rgba)
    {
        const float inv = 1f / 255f;
        return new Vector4(
            (rgba & 0x000000ff) * inv,
            ((rgba >> 8) & 0x000000ff) * inv,
            ((rgba >> 16) & 0x000000ff) * inv,
            ((rgba >> 24) & 0x000000ff) * inv);
    }

    private static bool TryBuildIndices(RmlUiRenderCommand command, out uint[] indices)
    {
        indices = new uint[command.IndexCount];
        int[] signedIndices = new int[command.IndexCount];
        Marshal.Copy(command.Indices, signedIndices, 0, command.IndexCount);

        for (int i = 0; i < signedIndices.Length; i++)
        {
            if (signedIndices[i] < 0 || signedIndices[i] >= command.VertexCount)
            {
                indices = Array.Empty<uint>();
                return false;
            }

            indices[i] = unchecked((uint)signedIndices[i]);
        }

        return true;
    }

    private static bool Vector4IsFinite(Vector4 value)
        => float.IsFinite(value.X) &&
           float.IsFinite(value.Y) &&
           float.IsFinite(value.Z) &&
           float.IsFinite(value.W);

    private static void ApplyScissor(CommandList cl, RmlUiRenderCommand command, RmlUiRenderData renderData)
    {
        if (command.ScissorEnabled == 0)
        {
            ResetScissor(cl, renderData);
            return;
        }

        uint x = (uint)Math.Clamp(command.ScissorX, 0, Math.Max(0, renderData.ViewportWidth));
        uint y = (uint)Math.Clamp(command.ScissorY, 0, Math.Max(0, renderData.ViewportHeight));
        uint width = (uint)Math.Clamp(command.ScissorWidth, 0, Math.Max(0, renderData.ViewportWidth - (int)x));
        uint height = (uint)Math.Clamp(command.ScissorHeight, 0, Math.Max(0, renderData.ViewportHeight - (int)y));
        cl.SetScissorRect(0, x, y, width, height);
    }

    private static void ResetScissor(CommandList cl, RmlUiRenderData renderData)
    {
        cl.SetScissorRect(
            0,
            0,
            0,
            (uint)Math.Max(1, renderData.ViewportWidth),
            (uint)Math.Max(1, renderData.ViewportHeight));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct UiVertex
    {
        public readonly Vector2 Position;
        public readonly Vector2 TexCoord;
        public readonly Vector4 Color;

        public UiVertex(Vector2 position, Vector2 texCoord, Vector4 color)
        {
            Position = position;
            TexCoord = texCoord;
            Color = color;
        }

        public static uint SizeInBytes => (uint)(8 * sizeof(float));
    }

    private readonly struct PreparedCommand
    {
        public readonly RmlUiRenderCommand Command;
        public readonly int VertexStart;
        public readonly int IndexStart;
        public readonly int IndexCount;

        public PreparedCommand(RmlUiRenderCommand command, int vertexStart, int indexStart, int indexCount)
        {
            Command = command;
            VertexStart = vertexStart;
            IndexStart = indexStart;
            IndexCount = indexCount;
        }
    }
}
