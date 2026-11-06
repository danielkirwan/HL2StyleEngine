using System.Runtime.InteropServices;
namespace Engine.Runtime.Audio;

internal static class NativeAudio
{
    private const string Dll = "HS2AudioBridge";
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern nint hs2_audio_create(int output);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void hs2_audio_destroy(nint a);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int hs2_audio_ready(nint a);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int hs2_audio_output(nint a, int enabled);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern nint hs2_audio_source(uint rate, uint channels, uint frames, int stream, float[]? pcm);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void hs2_audio_free(nint s);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern uint hs2_audio_space(nint s);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern uint hs2_audio_write(nint s, float[] pcm, uint frames, int eof);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong hs2_audio_starved(nint s);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern uint hs2_audio_play(nint a, nint s, int spatial, int loop, float x, float y, float z, float gain, float pitch, float near, float far);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void hs2_audio_stop(nint a, uint v);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int hs2_audio_valid(nint a, uint v);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void hs2_audio_voice(nint a, uint v, float x, float y, float z, float gain, int pause);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void hs2_audio_listener(nint a, float x, float y, float z, float fx, float fy, float fz, float ux, float uy, float uz);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void hs2_audio_stats(nint a, [Out] ulong[] stats);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int hs2_audio_render(nint a, [Out] float[] pcm, uint frames);
}
