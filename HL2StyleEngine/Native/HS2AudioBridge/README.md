# HS2 Audio Bridge

Windows x64 C ABI for the C# audio service. This is not a runtime migration.

- SoLoud core/NULL backend pinned to `e82fd32c1f62183922f08c14c814a02b58db1873`.
- Its bundled miniaudio 0.10.42 provides instance-owned WASAPI output.
- Upstream sources are unmodified; our PCM ring source and output adapter live
  in HS2AudioBridge.cpp. Upstream SoLoud WASAPI/miniaudio wrappers are not built.
- SoLoud uses the zlib licence; miniaudio's MIT No Attribution alternative is
  selected. Both notices are copied to build/publish output under Licenses.

Build with Visual Studio 2022 C++ tools and CMake:

```powershell
./Native/HS2AudioBridge/Build.ps1 -Fetch
```

Fetch uses the official pinned GitHub archive and stores the ignored source in
Native/vendor. Subsequent builds are offline. Build.ps1 copies the Release DLL
to Native/runtimes/win-x64, which Engine.Runtime deploys through its references.
The bridge uses the static MSVC runtime. Other platforms/architectures are not
implemented; unavailable binaries/devices leave the managed service silent
with diagnostics rather than changing gameplay.

The C# control worker owns voices, PCM files, prefetch and destruction. The
native callback mixes prepared samples only. No managed callback or file read
runs on the audio thread. A 32-voice limit and bounded 24,000-frame rings keep
work/memory bounded. Callback/offline requests are chunked to 512 frames to
respect SoLoud scratch capacity. Never invoke offline rendering on a live device.

Device retry and simulated output stop/reopen are supported. Physical unplug,
default-device switching and end-to-end acoustic latency remain hardware QA;
the reported period/callback duration is not a measurement of speaker latency.
See Game/AUDIO_IMPLEMENTATION.md for evidence and limitations.
