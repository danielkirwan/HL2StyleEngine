using System.Reflection;
using Engine.Runtime.Hosting;
using Engine.Render;
using Engine.Input.Devices;
using Game;
using Game.Inventory;
using Veldrid;

// Opt-in QA fixture. It uses the real game/UI without changing authored levels or saves.
internal sealed class InventoryPreview(string root) : IGameModule, IWorldRenderer, IOverlayRenderer, IInputConsumer
{
    private readonly HL2GameModule _game = new(Path.Combine(root, "Game/Content/Levels/sixRoomTest.json"));
    public InputState InputState => _game.InputState;
    public void Initialize(EngineContext context)
    {
        _game.Initialize(context);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var inventory = (InventoryContainer)typeof(HL2GameModule).GetField("_inventory", flags)!.GetValue(_game)!;
        inventory.Clear();
        inventory.Add(ItemCatalog.InkRibbon, 5);
        inventory.Add(ItemCatalog.MasterKey);
        inventory.Add(ItemCatalog.Scrap, 78);
        inventory.Add(ItemCatalog.Gunpowder, 14);
        inventory.Add(ItemCatalog.CrankHandle);
        inventory.Add(ItemCatalog.DamagedCable);
        inventory.Add(ItemCatalog.SpareWire);
        inventory.Add(ItemCatalog.Fuse);
        inventory.MoveStackToSlot(inventory.Stacks.Last().SlotIndex, inventory.PrimarySlotCapacity);
        typeof(HL2GameModule).GetMethod("SetInventoryOpen", flags)!.Invoke(_game, [true]);
    }
    public void Update(float dt, InputSnapshot input) => _game.Update(dt, input);
    public void FixedUpdate(float dt) => _game.FixedUpdate(dt);
    public void DrawImGui() => _game.DrawImGui();
    public void RenderWorld(Renderer renderer) => _game.RenderWorld(renderer);
    public void RenderOverlay(Renderer renderer) => _game.RenderOverlay(renderer);
    public void Dispose() => _game.Dispose();
}
