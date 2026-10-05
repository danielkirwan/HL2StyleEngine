using System.Numerics;
using System.Reflection;
using Engine.Input.Devices;
using Engine.UI;
using Game;
using Game.Inventory;
using Veldrid;
using Engine.Input.Actions;
using ImGuiNET;

internal static class InventoryInputChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static object? Call(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, Flags)!.Invoke(target, args);
    private static T Field<T>(object target, string name)
        => (T)target.GetType().GetField(name, Flags)!.GetValue(target)!;
    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, Flags)!.SetValue(target, value);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    internal static void Run(string root)
    {
        using var backend = RmlUiBackend.Probe(Path.Combine(root, "Game/bin/Debug/net10.0/Content/UI"),
            Path.Combine(root, "Game/bin/Debug/net10.0/HS2RmlUiBridge.dll"));
        var ui = new GameplayUiLayer(backend, nativePresentationEnabled: true);
        var module = new HL2GameModule();
        Call(module, "BuildActions");
        Set(module, "_gameplayUi", ui);
        Set(module, "_inventoryOpen", true);
        var inventory = Field<InventoryContainer>(module, "_inventory");
        var input = Field<InputState>(module, "_inputState");
        Require(inventory.Add(ItemCatalog.DamagedCable, 1) && inventory.Add(ItemCatalog.SpareWire, 1), "Cannot seed repair ingredients.");
        Set(module, "_selectedInventoryStackIndex", inventory.Stacks[0].SlotIndex);

        void Frame(Vector2 mouse, Key? key = null, params MouseEvent[] buttons)
        {
            Set(module, "_inventoryActionHandledThisFrame", false);
            Set(module, "_inventoryToggleConsumedThisFrame", false);
            Set(module, "_inventorySplitHandledThisFrame", false);
            input.Update(new Snapshot(mouse, key, buttons));
            Call(module, "UpdateInventoryNavigation");
            ui.SubmitState(new GameplayUiState
            {
                InventoryOpen = true,
                GridWidth = inventory.GridWidth,
                GridHeight = inventory.GridHeight,
                PrimaryGridHeight = inventory.PrimaryGridHeight,
                ViewportWidth = 1920, ViewportHeight = 1080,
                SelectedSlot = Field<int>(module, "_selectedInventoryStackIndex"),
                CombiningInventoryItem = Field<int>(module, "_combineSourceSlot") >= 0,
                CombineSourceSlot = Field<int>(module, "_combineSourceSlot"),
                InventoryActionMenuOpen = Field<bool>(module, "_inventoryActionMenuOpen"),
                SelectedInventoryActionIndex = Field<int>(module, "_inventoryActionIndex"),
                InventoryActionLabels = Field<string[]>(module, "InventoryActionLabels"),
                InventoryItems = (List<GameplayUiInventoryItem>)Call(module, "BuildInventoryUiItems")!
            });
            ui.Update(new RmlUiFrameContext(null!, input, 1920, 1080, 1f / 60));
            Require(ui.IsReady, "Native UI did not initialize: " + ui.Status);
            // Hit testing uses the real native document, but this CPU check has no GPU submission.
            object overlay = Field<object>(backend, "_overlayRenderer");
            overlay.GetType().GetProperty("LastSubmittedCommands", Flags)!.SetValue(overlay, 1);
            Call(module, "DrawGameplayHud");
        }

        var layout = new InventoryLayout(1920, 1080, 8, 8, 4);
        Vector2 source = layout.SlotCenter(0), target = layout.SlotCenter(1);
        Frame(source); Frame(source);
        Require(backend.TryGetHoveredDataSlot(out int hovered) && hovered == 0, "Source slot hit test failed: " + hovered);
        Frame(source, Key.E);
        for (int i = 0; i < 3; i++) Frame(source, Key.S);
        Frame(source, Key.E);
        Require(!Field<bool>(module, "_inventoryActionMenuOpen") && Field<int>(module, "_combineSourceSlot") == 0, "Menu did not enter Combine mode.");
        Frame(target); Frame(target);
        Require(backend.TryGetHoveredDataSlot(out hovered) && hovered == 1, "Target slot hit test failed: " + hovered);
        Frame(target, null, new MouseEvent(MouseButton.Left, true));
        Frame(target, null, new MouseEvent(MouseButton.Left, false));
        Require(inventory.Contains(ItemCatalog.RepairedCable), "Menu -> second-item click did not repair cable.");
        Console.WriteLine("PASS: native menu -> second-item mouse click repairs the cable.");

        Vector2 FindNativeSlot(int slot)
        {
            for (int y = 320; y < 800; y += 12)
                for (int x = 750; x < 1200; x += 12)
                {
                    Vector2 point = new(x, y);
                    input.Update(new Snapshot(point, null, []));
                    backend.Update(new RmlUiFrameContext(null!, input, 1920, 1080, 1f / 60));
                    if (backend.TryGetHoveredDataSlot(out int hit) && hit == slot) return point;
                }
            throw new InvalidDataException("Cannot locate native action row " + slot);
        }

        void Reset(string first, string second)
        {
            inventory.Clear();
            Call(module, "CloseInventoryActionUi");
            Set(module, "_movingInventoryFromSlot", -1);
            Require(inventory.Add(first, 1) && inventory.Add(second, 1), "Cannot seed pair.");
            Set(module, "_selectedInventoryStackIndex", 0);
            Frame(source); Frame(source);
        }

        MouseEvent[] click = [new(MouseButton.Left, true), new(MouseButton.Left, false)];
        foreach (bool reverse in new[] { false, true })
        {
            Reset(reverse ? ItemCatalog.SpareWire : ItemCatalog.DamagedCable,
                reverse ? ItemCatalog.DamagedCable : ItemCatalog.SpareWire);
            Frame(source, Key.E);
            Vector2 combine = FindNativeSlot(2003);
            Frame(combine); Frame(combine);
            Frame(combine, null, click);
            Require(Field<int>(module, "_combineSourceSlot") == 0 && !Field<bool>(module, "_inventoryActionMenuOpen"),
                "Quick click on native Combine menu failed.");
            Require(Field<int>(module, "_selectedInventoryStackIndex") == 0, "Action row leaked into grid selection.");
            Frame(target, null, click);
            Require(inventory.GetCount(ItemCatalog.RepairedCable) == 1 && inventory.StackCount == 1 &&
                    Field<int>(module, "_movingInventoryFromSlot") < 0,
                "Quick second-item click failed or started a drag (reverse=" + reverse + ").");
            Frame(target);
            Require(inventory.GetCount(ItemCatalog.RepairedCable) == 1, "Click combined more than once.");
        }
        Console.WriteLine("PASS: quick menu/target clicks, either ingredient order, no leaked selection or drag.");

        Reset(ItemCatalog.DamagedCable, ItemCatalog.SpareWire);
        Frame(source, Key.E);
        Vector2 combineClick = FindNativeSlot(2003);
        Vector2 oldHover = FindNativeSlot(2000);
        Frame(oldHover); Frame(oldHover);
        Frame(combineClick, null, click);
        Require(Field<int>(module, "_combineSourceSlot") == 0, "Move-and-click used the previous frame's menu row.");
        Frame(target, null, click);
        Require(inventory.Contains(ItemCatalog.RepairedCable), "Move-and-click repair failed.");
        Console.WriteLine("PASS: native menu clicks use current-frame hit testing.");

        Reset(ItemCatalog.DamagedCable, ItemCatalog.SpareWire);
        Frame(source, Key.E);
        Vector2 use = FindNativeSlot(2000);
        Frame(use); Frame(use);
        for (int i = 0; i < 3; i++) Frame(use, Key.S);
        Frame(use, Key.E);
        Require(Field<int>(module, "_combineSourceSlot") == 0, "Stationary menu hover blocked keyboard Combine.");
        Frame(source); Frame(source);
        Frame(source, Key.D); Frame(source);
        Require(Field<int>(module, "_selectedInventoryStackIndex") == 1, "Stationary grid hover stole keyboard target.");
        Frame(source, Key.E);
        Require(inventory.Contains(ItemCatalog.RepairedCable), "Keyboard target confirmation failed.");
        Console.WriteLine("PASS: keyboard menu and target selection with a stationary mouse.");

        Reset(ItemCatalog.DamagedCable, ItemCatalog.SpareWire);
        void Pad(GamepadButton button)
        {
            Set(input, "_controller", new IntPtr(1));
            var pressed = Field<Dictionary<GamepadButton, bool>>(input, "_padPressed");
            pressed[button] = true;
            Frame(source);
            pressed.Clear();
        }
        Pad(GamepadButton.X);
        for (int i = 0; i < 3; i++) Pad(GamepadButton.DpadDown);
        Pad(GamepadButton.X);
        Pad(GamepadButton.DpadRight); Frame(source);
        Pad(GamepadButton.X);
        Set(input, "_controller", IntPtr.Zero);
        Require(inventory.Contains(ItemCatalog.RepairedCable), "Controller Combine/target selection failed.");
        Console.WriteLine("PASS: controller menu, target selection and X confirmation.");

        Reset(ItemCatalog.DamagedCable, ItemCatalog.SpareWire);
        Require(inventory.Add(ItemCatalog.InkRibbon, 1), "Cannot seed invalid target.");
        Frame(source, Key.E);
        for (int i = 0; i < 3; i++) Frame(source, Key.S);
        Frame(source, Key.E);
        Frame(source, null, click);
        Frame(layout.SlotCenter(2), null, click);
        Frame(layout.SlotCenter(3), null, click);
        Require(inventory.StackCount == 3 && Field<int>(module, "_combineSourceSlot") == 0, "Invalid/self target consumed ingredients.");
        Frame(target, Key.Escape);
        Require(inventory.StackCount == 3 && Field<int>(module, "_combineSourceSlot") < 0, "Cancel consumed ingredients.");
        Frame(target, null, click);
        Require(Field<int>(module, "_movingInventoryFromSlot") < 0, "A quick inventory click left an unfinished drag.");
        Console.WriteLine("PASS: invalid/empty/self target, cancel and quick-click drag cleanup.");

        Reset(ItemCatalog.Scrap, ItemCatalog.Gunpowder);
        object weapons = Field<object>(module, "_weaponSystem");
        object pistol = Field<System.Collections.IEnumerable>(weapons, "_definitions").Cast<object>()
            .Single(w => (string?)w.GetType().GetProperty("AmmoItemId")!.GetValue(w) == ItemCatalog.Bullets);
        int AmmoCount()
        {
            object snapshot = Call(weapons, "GetAmmoSnapshot", pistol)!;
            return (int)snapshot.GetType().GetProperty("TotalAmmo")!.GetValue(snapshot)!;
        }
        int ammoBefore = AmmoCount();
        Frame(source, Key.E);
        for (int i = 0; i < 3; i++) Frame(source, Key.S);
        Frame(source, Key.E);
        Frame(target, null, click);
        Require(inventory.IsEmpty && AmmoCount() == ammoBefore + 12 &&
                Field<string>(module, "_gameMessage").Contains("Created Bullets x12"),
            "Crafting did not deliver ammo directly to weapon reserves with a notification.");
        Console.WriteLine("PASS: existing ammo crafting bypasses inventory and reports the created amount.");

        Reset(ItemCatalog.DamagedCable, ItemCatalog.SpareWire);
        Require(inventory.MoveStackToSlot(0, 32), "Cannot seed overflow drag.");
        Vector2 overflow = layout.SlotCenter(32), destination = layout.SlotCenter(7);
        Frame(overflow); Frame(overflow);
        Frame(overflow, null, new MouseEvent(MouseButton.Left, true));
        Frame(destination, null, new MouseEvent(MouseButton.Left, true));
        Frame(destination, null, new MouseEvent(MouseButton.Left, false));
        Require(inventory.GetStackCoveringSlot(7)?.ItemId == ItemCatalog.DamagedCable &&
            !inventory.OverflowStacks.Any(), "Native overflow drag did not reach destination.");
        Console.WriteLine("PASS: native overflow-to-inventory drag.");

        var quickClick = new InputState();
        quickClick.Update(new Snapshot(target, null,
            [new MouseEvent(MouseButton.Left, true), new MouseEvent(MouseButton.Left, false)]));
        Require(quickClick.LeftMousePressedThisFrame && quickClick.LeftMouseReleasedThisFrame && !quickClick.LeftMouseDown,
            "A complete click received in one frame was lost.");
        quickClick.Update(new Snapshot(target, null, []));
        Require(!quickClick.LeftMousePressedThisFrame && !quickClick.LeftMouseReleasedThisFrame, "Click edges repeated next frame.");
        foreach (MouseButton button in new[] { MouseButton.Left, MouseButton.Right })
        {
            quickClick.Update(new Snapshot(target, null, [new(button, true)]));
            Require(button == MouseButton.Left ? quickClick.LeftMousePressedThisFrame : quickClick.RightMousePressedThisFrame, "Mouse down edge missing.");
            quickClick.Update(new Snapshot(target, null, [new(button, true)]));
            Require(button == MouseButton.Left ? quickClick.LeftMouseDown && !quickClick.LeftMousePressedThisFrame :
                quickClick.RightMouseDown && !quickClick.RightMousePressedThisFrame, "Held/repeated mouse down produced another press.");
            quickClick.Update(new Snapshot(target, null, [new(button, false), new(button, true)]));
            Require(button == MouseButton.Left ? quickClick.LeftMouseDown && quickClick.LeftMousePressedThisFrame && quickClick.LeftMouseReleasedThisFrame :
                quickClick.RightMouseDown && quickClick.RightMousePressedThisFrame && quickClick.RightMouseReleasedThisFrame, "Release/repress edge missing.");
            quickClick.Update(new Snapshot(target, null, [new(button, false)]));
            Require(button == MouseButton.Left ? !quickClick.LeftMouseDown && quickClick.LeftMouseReleasedThisFrame :
                !quickClick.RightMouseDown && quickClick.RightMouseReleasedThisFrame, "Mouse up edge missing.");
        }
        Console.WriteLine("PASS: same-frame mouse press/release edges are retained.");
        CheckPreview(root);
    }

    private static unsafe void CheckPreview(string root)
    {
        IntPtr context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.NativePtr->IniFilename = null;
            io.DisplaySize = new Vector2(1920, 1080);
            io.DeltaTime = 1f / 60;
            io.Fonts.AddFontDefault();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
            using var ui = new GameplayUiLayer(RmlUiBackend.Probe(Path.Combine(root, "Tools/LevelAuthoring/bin/Debug/net10.0/Content/UI"),
                "UnavailableInventoryTestBridge"), false);
            var module = new HL2GameModule();
            Call(module, "BuildActions");
            Set(module, "_gameplayUi", ui);
            Set(module, "_inventoryOpen", true);
            var inventory = Field<InventoryContainer>(module, "_inventory");
            var input = Field<InputState>(module, "_inputState");
            inventory.Add(ItemCatalog.DamagedCable, 1);
            inventory.Add(ItemCatalog.SpareWire, 1);
            Set(module, "_selectedInventoryStackIndex", 0);
            void Frame(Vector2 pointer, Key? key = null, params MouseEvent[] buttons)
            {
                Set(module, "_inventoryActionHandledThisFrame", false);
                input.Update(new Snapshot(pointer, key, buttons));
                Call(module, "UpdateInventoryNavigation");
                ui.SubmitState(new GameplayUiState
                {
                    InventoryOpen = true,
                    GridWidth = inventory.GridWidth, GridHeight = inventory.GridHeight,
                    PrimaryGridHeight = inventory.PrimaryGridHeight,
                    ViewportWidth = 1920, ViewportHeight = 1080,
                    InventoryActionMenuOpen = Field<bool>(module, "_inventoryActionMenuOpen"),
                    SelectedSlot = Field<int>(module, "_selectedInventoryStackIndex"),
                    InventoryItems = (List<GameplayUiInventoryItem>)Call(module, "BuildInventoryUiItems")!,
                    CombiningInventoryItem = Field<int>(module, "_combineSourceSlot") >= 0
                });
                io.MousePos = pointer;
                io.MouseDown[0] = input.LeftMouseDown;
                ImGui.NewFrame();
                Call(module, "DrawGameplayHud");
                ImGui.Render();
            }
            var layout = new InventoryLayout(1920, 1080, 8, 8, 4);
            Vector2 source = layout.SlotCenter(0), target = layout.SlotCenter(1);
            Frame(source); Frame(source);
            Frame(source, Key.E);
            Frame(target);
            Require(Field<int>(module, "_selectedInventoryStackIndex") == 0, "Preview hover changed the menu's source item.");
            for (int i = 0; i < 3; i++) Frame(target, Key.S);
            Frame(target, Key.E);
            Require(Field<int>(module, "_combineSourceSlot") == 0, "Preview menu did not preserve Combine source.");
            Frame(target, null, new MouseEvent(MouseButton.Left, true), new MouseEvent(MouseButton.Left, false));
            Require(inventory.Contains(ItemCatalog.RepairedCable), "Preview second-item quick click failed.");
            Console.WriteLine("PASS: ImGui fallback menu preserves source and accepts quick target clicks.");
            Require(inventory.MoveStackToSlot(inventory.Stacks[0].SlotIndex, 32), "Cannot seed preview overflow drag.");
            Vector2 overflow = layout.SlotCenter(32), destination = layout.SlotCenter(7);
            Frame(overflow); Frame(overflow);
            Frame(overflow, null, new MouseEvent(MouseButton.Left, true));
            Frame(destination, null, new MouseEvent(MouseButton.Left, true));
            Frame(destination, null, new MouseEvent(MouseButton.Left, false));
            Require(inventory.GetStackCoveringSlot(7)?.ItemId == ItemCatalog.RepairedCable &&
                !inventory.OverflowStacks.Any(), "Preview overflow drag did not reach destination.");
            Console.WriteLine("PASS: ImGui fallback overflow-to-inventory drag.");
        }
        finally { ImGui.DestroyContext(context); }
    }

    private sealed class Snapshot(Vector2 position, Key? key, MouseEvent[] buttons) : InputSnapshot
    {
        public IReadOnlyList<KeyEvent> KeyEvents { get; } = key.HasValue
            ? [new KeyEvent(key.Value, true, ModifierKeys.None), new KeyEvent(key.Value, false, ModifierKeys.None)] : [];
        public IReadOnlyList<MouseEvent> MouseEvents => buttons;
        public IReadOnlyList<char> KeyCharPresses => Array.Empty<char>();
        public Vector2 MousePosition => position;
        public float WheelDelta => 0;
        public bool IsMouseDown(MouseButton button) => buttons.LastOrDefault(e => e.MouseButton == button).Down;
    }
}
