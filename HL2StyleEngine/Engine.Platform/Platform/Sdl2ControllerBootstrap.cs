using System;
using System.Runtime.InteropServices;

namespace Engine.Platform;

public static class Sdl2ControllerBootstrap
{
    private const uint SDL_INIT_JOYSTICK = 0x00000200;
    private const uint SDL_INIT_GAMECONTROLLER = 0x00002000;

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint SDL_WasInit(uint flags);

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_InitSubSystem(uint flags);

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetError();

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_NumJoysticks();

    private static bool _done;

    public static void EnsureControllersInitialized()
    {
        if (_done) return;
        _done = true;

        try
        {
            uint was = SDL_WasInit(0);

            int rJoy = SDL_InitSubSystem(SDL_INIT_JOYSTICK);
            int rGc = SDL_InitSubSystem(SDL_INIT_GAMECONTROLLER);

            uint now = SDL_WasInit(0);
            int num = SDL_NumJoysticks();

            Console.WriteLine($"[SDL] WasInit(before)=0x{was:X}  Init(JOY)={rJoy}  Init(GC)={rGc}  WasInit(after)=0x{now:X}");
            Console.WriteLine($"[SDL] NumJoysticks after init: {num}");
            Console.WriteLine($"[SDL] Error: {GetErrorString()}");
        }
        catch (DllNotFoundException e)
        {
            Console.WriteLine($"[SDL] SDL2.dll not found: {e.Message}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[SDL] Controller bootstrap failed: {e}");
        }
    }

    private static string GetErrorString()
    {
        IntPtr p = SDL_GetError();
        return p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p) ?? "";
    }
}
