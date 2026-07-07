using Engine.Runtime.Hosting;

namespace HS2Editor;

internal static class Program
{
    private static void Main()
    {
        using var host = new EngineHost(1600, 900, "HS2Editor");
        using var editor = new HS2EditorModule();
        host.Run(editor);
    }
}
