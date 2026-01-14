# HL2-Style Engine (C# / Veldrid)

![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=csharp&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![Veldrid](https://img.shields.io/badge/Veldrid-Rendering-blue?style=for-the-badge)
![ImGui](https://img.shields.io/badge/ImGui-Debug%20UI-orange?style=for-the-badge)
![Engine](https://img.shields.io/badge/Focus-Custom%20Game%20Engine-red?style=for-the-badge)

A **custom C# game engine** inspired by *Half-Life 2–style* architecture, built from the ground up using **.NET 8**, **Veldrid**, and **ImGui**, with a strong focus on **clean separation between engine and game code**, fast iteration, and long-term scalability.

---

## ✨ Features

- Custom engine architecture (no Unity / Unreal)
- Clear separation between **Engine** and **Game**
- Cross-API rendering via **Veldrid**
- Immediate-mode debug UI with **ImGui**
- FPS-style camera and movement foundations
- Fixed-timestep simulation loop
- SDL2 windowing and input
- Designed for **fast compile times**
- Editor-friendly, iteration-focused structure

---

## 📄 Overview

This project explores how **FPS-style engines are structured internally**, inspired by classic Source-engine concepts but implemented using modern **C# and .NET**.

Key goals:

- **No engine–game coupling**  
  Games consume engine APIs they never modify engine internals.
- **Fast iteration**  
  Small rebuilds, minimal recompiles, clear module boundaries.
- **Explicit systems**  
  Rendering, input, movement, timing, and UI are explicit systems.
- **Editor-ready foundations**  
  Designed to support in-game editing and tooling later.

This is a **learning project**.

---

## 🧱 Architecture

The solution is split into clearly defined projects:

```text
Engine.Core      → Math, time, shared utilities
Engine.Platform  → Windowing, input, OS abstraction
Engine.Render    → Rendering, shaders, GPU resources
Engine.Runtime   → Engine host, main loop, lifecycle
Game             → Game-specific code (uses engine APIs)
```
## 🎮 Runtime Flow

High-level execution flow:

1. Engine creates window and graphics device  
2. Game module is initialized  
3. Main loop:
   - Pump OS events
   - Update input
   - Per-frame update
   - Fixed-timestep simulation
   - Render world
   - Render ImGui debug UI
4. Clean shutdown and disposal

This mirrors how professional engines structure their runtime.

---

## 🖥 Rendering

- Backend-agnostic rendering via **Veldrid**
- Currently targeting **Direct3D11 (Windows)**
- Explicit command lists and pipelines
- World rendering and ImGui rendering are separated

Designed to expand into:
- Depth buffering
- Multiple render passes
- Debug overlays
- Editor gizmos

---

## 🧭 Input & Camera

- SDL2-based input
- Relative mouse input for FPS camera
- Source-inspired movement foundations
- Fixed-timestep movement simulation
- Camera cleanly decoupled from rendering

---

## 📦 Requirements

- Windows
- .NET SDK **8.0+**
- Visual Studio 2022 (recommended)

---

## 🚀 Getting Started

1. Clone the repository
2. Open the solution in **Visual Studio 2022**
3. Restore NuGet packages
4. Set **Game** as the startup project
5. Build and run

The engine will launch a window with a basic world and debug UI.

---

## 🛠 Current Status

**Actively developed**

**Implemented:**
- Engine / game separation
- Windowing & input
- Rendering pipeline
- ImGui integration
- FPS camera & movement base

**Planned:**
- Depth buffer & resize handling
- Basic level geometry
- In-game editor tools
- Scene serialization
- Hot-reloadable content
- Asset pipeline

---

## 📌 Motivation

This project exists to:
- Deepen understanding of real engine architecture
- Avoid black-box engines
- Build reusable, testable systems
- Demonstrate low-level engine knowledge for portfolio and interviews

