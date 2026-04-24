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
    private static extern int SDL_NumJoysticks();

    private static bool _done;

    public static void EnsureControllersInitialized()
    {
        if (_done) return;
        _done = true;

        try
        {
            SDL_WasInit(0);
            SDL_InitSubSystem(SDL_INIT_JOYSTICK);
            SDL_InitSubSystem(SDL_INIT_GAMECONTROLLER);
            SDL_WasInit(0);
            SDL_NumJoysticks();
        }
        catch (DllNotFoundException)
        {
        }
        catch
        {
        }
    }
}
