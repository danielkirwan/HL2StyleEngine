using System.Runtime.InteropServices;
using Veldrid;
using Vortice.Direct3D11;

namespace Engine.Render;

// Diagnostic only. Results are polled on later frames, never flushed or waited for.
public sealed class GpuFrameProfile : IDisposable
{
    public readonly record struct Sample(int Frame, double? Milliseconds, int ResultAgeFrames, string Status);
    private sealed class Slot : IDisposable
    {
        internal readonly ID3D11Query Clock, Begin, End;
        internal int Frame = -1;
        internal Slot(ID3D11Device device)
        {
            Clock = device.CreateQuery(new(QueryType.TimestampDisjoint));
            try { Begin = device.CreateQuery(new(QueryType.Timestamp)); }
            catch { Clock.Dispose(); throw; }
            try { End = device.CreateQuery(new(QueryType.Timestamp)); }
            catch { Begin.Dispose(); Clock.Dispose(); throw; }
        }
        public void Dispose() { Clock.Dispose(); Begin.Dispose(); End.Dispose(); }
    }
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly List<Slot> _slots = new(16);
    private readonly List<Sample> _samples;
    private readonly int _limit;
    private Slot? _active;
    private int _issued;
    private bool _disposed;
    public IReadOnlyList<Sample> Samples => _samples;
    public int Pending => _slots.Count(s => s.Frame >= 0);
    public int Skipped { get; private set; }
    public string DeviceFlags => _device.CreationFlags.ToString();
    public GpuFrameProfile(GraphicsDevice device, int sampleLimit)
    {
        if (device.BackendType != GraphicsBackend.Direct3D11) throw new NotSupportedException("GPU trace requires D3D11.");
        _limit = Math.Clamp(sampleLimit, 1, 36120);
        _samples = new(_limit);
        var pointer = device.GetD3D11Info().Device;
        Marshal.AddRef(pointer);
        _device = new(pointer);
        _context = _device.ImmediateContext;
        try { for (int i = 0; i < 16; i++) _slots.Add(new(_device)); }
        catch { Dispose(); throw; }
    }
    public void Begin(int frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_active != null) throw new InvalidOperationException("GPU sample already active.");
        if (_issued >= _limit) return;
        _active = _slots.Find(s => s.Frame < 0);
        if (_active == null) { Skipped++; return; }
        _issued++;
        _active.Frame = frame;
        _context.Begin(_active.Clock);
        _context.End(_active.Begin);
    }
    public void End()
    {
        if (_active == null) return;
        _context.End(_active.End);
        _context.End(_active.Clock);
        _active = null;
    }
    public unsafe void Poll(int frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var slot in _slots)
        {
            if (slot.Frame < 0 || ReferenceEquals(slot, _active)) continue;
            QueryDataTimestampDisjoint clock = default;
            ulong begin = 0, end = 0;
            int c = _context.GetData(slot.Clock, (IntPtr)(&clock), sizeof(QueryDataTimestampDisjoint), AsyncGetDataFlags.DoNotFlush).Code;
            if (c == 1) continue; // S_FALSE: try on the next frame, without stalling.
            int b = c == 0 ? _context.GetData(slot.Begin, (IntPtr)(&begin), sizeof(ulong), AsyncGetDataFlags.DoNotFlush).Code : c;
            int e = c == 0 ? _context.GetData(slot.End, (IntPtr)(&end), sizeof(ulong), AsyncGetDataFlags.DoNotFlush).Code : c;
            if (b == 1 || e == 1) continue;
            bool valid = c == 0 && b == 0 && e == 0 && !clock.Disjoint && clock.Frequency > 0 && end >= begin;
            _samples.Add(new(slot.Frame, valid ? (end - begin) * 1000.0 / clock.Frequency : null,
                frame - slot.Frame, valid ? "ready" : $"invalid:{c}/{b}/{e};disjoint={clock.Disjoint}"));
            slot.Frame = -1;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        End();
        _disposed = true;
        foreach (var slot in _slots) slot.Dispose();
        _slots.Clear(); _context.Dispose(); _device.Dispose();
    }
}
