using System.Numerics;
using ImGuiNET;

namespace Game;

public sealed partial class HL2GameModule
{
    private bool _restoreEditorPanels;
    private bool _expandEditorPanels = true;

    private void DrawInGameEditorPanels()
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem("Debug / Weapon Tools", "F3", _debugWindowOpen))
                    _debugWindowOpen = !_debugWindowOpen;
                if (ImGui.MenuItem("Restore Editor Panels"))
                    _restoreEditorPanels = true;
                ImGui.EndMenu();
            }
            _mouseOverEditorUi |= ImGui.IsWindowHovered();
            _keyboardOverEditorUi |= ImGui.IsWindowFocused();
            ImGui.EndMainMenuBar();
        }

        var viewport = ImGui.GetMainViewport();
        Vector2 size = viewport.Size;
        float inspectorWidth = MathF.Min(420f, size.X * 0.34f);
        PrepareEditorPanel(viewport.Pos + new Vector2(12f, 32f),
            new Vector2(MathF.Min(600f, size.X - inspectorWidth - 36f), 260f));
        _editor.DrawToolbarPanel(ref _mouseOverEditorUi, ref _keyboardOverEditorUi);
        PrepareEditorPanel(viewport.Pos + new Vector2(12f, 304f),
            new Vector2(260f, MathF.Max(150f, size.Y - 316f)));
        _editor.DrawHierarchyPanel(ref _mouseOverEditorUi, ref _keyboardOverEditorUi);
        PrepareEditorPanel(viewport.Pos + new Vector2(size.X - inspectorWidth - 12f, 32f),
            new Vector2(inspectorWidth, MathF.Max(200f, size.Y - 44f)));
        _editor.DrawInspectorPanel(ref _mouseOverEditorUi, ref _keyboardOverEditorUi);
        _restoreEditorPanels = false;
        _expandEditorPanels = false;
    }

    private void PrepareEditorPanel(Vector2 position, Vector2 size)
    {
        if (_restoreEditorPanels)
            ImGui.SetNextWindowDockID(0, ImGuiCond.Always);
        ImGuiCond condition = _restoreEditorPanels ? ImGuiCond.Always : ImGuiCond.FirstUseEver;
        ImGui.SetNextWindowPos(position, condition);
        ImGui.SetNextWindowSize(size, condition);
        if (_restoreEditorPanels || _expandEditorPanels)
            ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
    }
}
