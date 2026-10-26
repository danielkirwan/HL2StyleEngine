using System.Numerics;
using Engine.Render;
using Engine.Editor.Level;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
if(args.Length>0 && args[0] is "build-render-stress" or "validate-render-stress" or "cook-render-stress" or "check-render-stress-pixels" or "benchmark-submission" or "validate-submission-package" or "capture-render-stress-views")
{
    try
    {
        switch(args[0])
        {
            case "build-render-stress": RenderStressTest.Build(root); break;
            case "validate-render-stress": RenderStressTest.Validate(root); break;
            case "cook-render-stress": RoomReflectionChecks.CaptureFixture(root,RenderStressTest.FileName); break;
            case "check-render-stress-pixels": RenderStressTest.Pixels(root); break;
            case "validate-submission-package": ReflectionStreamingChecks.Package(root,args[1],RenderStressTest.FileName); break;
            case "capture-render-stress-views":
                string output=Path.Combine(AppContext.BaseDirectory,"qa","stress-editor-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(output);PbrBaselineChecks.Capture(root,RenderStressTest.FileName,output,false,forceLegacy:false);Console.WriteLine(output);break;
            default: GameplayFrameChecks.Run(root,!args.Contains("--torch-off"),levelFile:args.Contains("--six-room")?RoomVisibilityChecks.FileName:RenderStressTest.FileName,
                residencyRoute:args.Contains("--route"),stress:args.Contains("--stress"),referenceSubmission:args.Contains("--reference")); break;
        }
    }
    catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if(args is ["validate-submission"] or ["validate-submission-capture"])
{
    try { if(args[0]=="validate-submission")SubmissionChecks.Run();else SubmissionChecks.Capture(root); }
    catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if(args is ["check-submission-pixels"])
{
    try { RoomVisibilityChecks.Pixels(root, submission:true); }
    catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if (args.Length > 0 && args[0] is "build-room-visibility" or "validate-room-visibility" or "check-room-visibility-pixels" or "benchmark-room-visibility" or "validate-room-visibility-package" or "capture-room-visibility")
{
    try
    {
        switch (args[0])
        {
            case "build-room-visibility": RoomVisibilityChecks.Build(root); break;
            case "validate-room-visibility": RoomVisibilityChecks.Validate(root); break;
            case "check-room-visibility-pixels": RoomVisibilityChecks.Pixels(root); break;
            case "validate-room-visibility-package": ReflectionStreamingChecks.Package(root,args[1],RoomVisibilityChecks.FileName); break;
            case "capture-room-visibility":
                string folder=Path.Combine(AppContext.BaseDirectory,"qa","room-editor-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(folder); PbrBaselineChecks.Capture(root,RoomVisibilityChecks.FileName,folder,false,forceLegacy:false); Console.WriteLine(folder); break;
            default:
                if(args.Length<2 || args[1] is not ("on" or "off")) throw new ArgumentException("Specify on|off and optional --stress, --route, --switches, --reference-visibility, --reference-rooms.");
                GameplayFrameChecks.Run(root,args[1]=="on",referenceVisibility:args.Contains("--reference-visibility"),stress:args.Contains("--stress"),
                    levelFile:RoomVisibilityChecks.FileName,residencyRoute:args.Contains("--route"),lightingSwitches:args.Contains("--switches"),referenceRooms:args.Contains("--reference-rooms")); break;
        }
    }
    catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if (args is ["diagnose-flashlight-mount"] or ["validate-flashlight-mount"])
{
    try { FlashlightMountChecks.Run(root, args[0] == "validate-flashlight-mount"); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if(args is ["diagnose-shadow-contact"] or ["validate-shadow-contact"])
{
    try{ShadowContactChecks.Run(args[0]=="validate-shadow-contact");}
    catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if (args is ["build-shadow-test"] or ["cook-shadow-state", "on" or "off"] or ["validate-shadow-test"] or
    ["validate-multi-shadows"] or ["check-shadow-pixels"] or ["capture-shadow-test"] or
    ["benchmark-shadow-test", "on" or "off"] or ["benchmark-shadow-route", "on" or "off"] or ["benchmark-shadow-switches", "on" or "off"])
{
    try
    {
        if(args[0]=="build-shadow-test")ShadowStabilityChecks.Build(root);
        else if(args[0]=="cook-shadow-state")LightingStabilityChecks.Cook(root,args[1],ShadowStabilityChecks.FileName);
        else if(args[0]=="validate-shadow-test")ShadowStabilityChecks.Validate(root);
        else if(args[0]=="validate-multi-shadows")RenderPipelineChecks.Run(pbr:true,multi:true);
        else if(args[0]=="check-shadow-pixels")ShadowStabilityChecks.Pixels(root);
        else if(args[0]=="capture-shadow-test")
        {
            string output=Path.Combine(AppContext.BaseDirectory,"qa","shadow-views-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output);PbrBaselineChecks.Capture(root,ShadowStabilityChecks.FileName,output,false,forceLegacy:false);Console.WriteLine(output);
        }
        else GameplayFrameChecks.Run(root,args[1]=="on",levelFile:ShadowStabilityChecks.FileName,
            stress:args[0]=="benchmark-shadow-test",residencyRoute:args[0]=="benchmark-shadow-route",lightingSwitches:args[0]=="benchmark-shadow-switches");
    }
    catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if (args is ["validate-shadow-package", var shadowPackage])
{
    try { ReflectionStreamingChecks.Package(root,shadowPackage,ShadowStabilityChecks.FileName); }
    catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if (args is ["build-lighting-stability"] or ["cook-lighting-state", "on" or "off"] or ["validate-lighting-stability"] or
    ["capture-lighting-stability"] or ["diagnose-updated-lighting"] or ["benchmark-lighting-stability", "on" or "off"] or
    ["benchmark-lighting-stability-route", "on" or "off"] or ["benchmark-lighting-switches", "on" or "off"] or ["check-lighting-switch-pixels"])
{
    try
    {
        if(args[0]=="build-lighting-stability")LightingStabilityChecks.Build(root);
        else if(args[0]=="cook-lighting-state")LightingStabilityChecks.Cook(root,args[1]);
        else if(args[0]=="validate-lighting-stability")LightingStabilityChecks.Validate(root);
        else if(args[0]=="check-lighting-switch-pixels")LightingStabilityChecks.SwitchPixels(root);
        else if(args[0]=="diagnose-updated-lighting")LightingStabilityChecks.Diagnose(root,LightingStabilityChecks.FileName);
        else if(args[0]=="capture-lighting-stability")
        {
            string output=Path.Combine(AppContext.BaseDirectory,"qa","lighting-stability-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output);PbrBaselineChecks.Capture(root,LightingStabilityChecks.FileName,output,false,forceLegacy:false);Console.WriteLine(output);
        }
        else GameplayFrameChecks.Run(root,args[1]=="on",levelFile:LightingStabilityChecks.FileName,
            stress:args[0]=="benchmark-lighting-stability",residencyRoute:args[0]=="benchmark-lighting-stability-route",lightingSwitches:args[0]=="benchmark-lighting-switches");
    }
    catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if (args is ["validate-stability-package", var stabilityPackage])
{
    try { ReflectionStreamingChecks.Package(root,stabilityPackage,LightingStabilityChecks.FileName); }
    catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if (args is ["diagnose-lighting-stability"])
{
    try { LightingStabilityChecks.Diagnose(root); }
    catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
    return;
}
if (args is ["build-point-light-test"] or ["capture-point-light-test"] or ["validate-point-light-test"] or
    ["check-point-light-pixels"] or ["capture-point-light-views"] or ["benchmark-point-lights", "on" or "off"] or
    ["benchmark-point-light-route", "on" or "off"])
{
    try
    {
        if (args[0] == "build-point-light-test") PointLightOnlyChecks.Build(root);
        else if (args[0] == "capture-point-light-test") RoomReflectionChecks.CaptureFixture(root,PointLightOnlyChecks.FileName);
        else if (args[0] == "validate-point-light-test") PointLightOnlyChecks.Validate(root);
        else if (args[0] == "check-point-light-pixels") PointLightOnlyChecks.CheckGpu(root);
        else if (args[0] == "capture-point-light-views")
        {
            string output = Path.Combine(AppContext.BaseDirectory,"qa","point-light-views-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output); PbrBaselineChecks.Capture(root,PointLightOnlyChecks.FileName,output,false,forceLegacy:false);
            Console.WriteLine(output);
        }
        else GameplayFrameChecks.Run(root,args[1]=="on",levelFile:PointLightOnlyChecks.FileName,
            stress:args[0]=="benchmark-point-lights",residencyRoute:args[0]=="benchmark-point-light-route");
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
    return;
}
if (args is ["validate-reflection-package", var packageDirectory])
{
    try { ReflectionStreamingChecks.Package(root, packageDirectory); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
    return;
}
if (args is ["validate-point-light-package", var pointLightPackage])
{
    try { ReflectionStreamingChecks.Package(root, pointLightPackage, PointLightOnlyChecks.FileName); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
    return;
}
if (args is ["build-six-room-pbr-test"] or ["capture-six-room-pbr"] or ["validate-reflection-streaming"] or
    ["capture-six-room-pbr-views"] or ["benchmark-six-room-pbr", "on" or "off"] or ["benchmark-six-room-pbr-route", "on" or "off"])
{
    try
    {
        if (args[0] == "build-six-room-pbr-test") ReflectionStreamingChecks.Build(root);
        else if (args[0] == "capture-six-room-pbr") RoomReflectionChecks.CaptureFixture(root, ReflectionStreamingChecks.FileName);
        else if (args[0] == "validate-reflection-streaming") ReflectionStreamingChecks.Run(root);
        else if (args[0] == "capture-six-room-pbr-views")
        {
            string output = Path.Combine(AppContext.BaseDirectory,"qa","six-room-pbr-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output); PbrBaselineChecks.Capture(root,ReflectionStreamingChecks.FileName,output,false,forceLegacy:false);
            Console.WriteLine(output);
        }
        else GameplayFrameChecks.Run(root,args[1]=="on",levelFile:ReflectionStreamingChecks.FileName,
            stress:args[0]=="benchmark-six-room-pbr",residencyRoute:args[0]=="benchmark-six-room-pbr-route");
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
    return;
}
if (args is ["build-room-transition-test"] or ["capture-room-transitions"] or ["validate-room-transitions"] or ["capture-room-transition-views"] or
    ["profile-room-transitions"] or ["profile-room-transitions", "--reverse"] or ["benchmark-gameplay-room-transitions", "on" or "off"])
{
    try
    {
        if(args[0]=="build-room-transition-test")RoomTransitionChecks.Build(root);
        else if(args[0]=="capture-room-transitions")RoomReflectionChecks.CaptureFixture(root,RoomTransitionChecks.FileName);
        else if(args[0]=="validate-room-transitions")RoomTransitionChecks.Run(root);
        else if(args[0]=="profile-room-transitions")PbrPerformanceChecks.Run(root,args.Contains("--reverse"),reflections:true,rooms:true,transitions:true);
        else if(args[0]=="capture-room-transition-views")
        {
            string output=Path.Combine(AppContext.BaseDirectory,"qa","room-transitions-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output);PbrBaselineChecks.Capture(root,RoomTransitionChecks.FileName,output,false,forceLegacy:false);
            Console.WriteLine(output);
        }
        else GameplayFrameChecks.Run(root,args[1]=="on",levelFile:RoomTransitionChecks.FileName);
    }
    catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if(args is ["profile-room-reflections"] or ["profile-room-reflections", "--reverse"])
{
    try {PbrPerformanceChecks.Run(root,args.Contains("--reverse"),reflections:true,rooms:true);}
    catch(Exception ex) {Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if (args is ["build-room-reflection-test"] or ["capture-room-reflections"] or ["capture-room-reflection-views"] or ["validate-room-reflections"] or ["benchmark-gameplay-room-reflections", "on" or "off"])
{
    try
    {
        if(args[0]=="build-room-reflection-test") RoomReflectionChecks.Build(root);
        else if(args[0]=="capture-room-reflections") RoomReflectionChecks.CaptureFixture(root);
        else if(args[0]=="validate-room-reflections") RoomReflectionChecks.Run(root);
        else if(args[0]=="capture-room-reflection-views")
        {
            string output=Path.Combine(AppContext.BaseDirectory,"qa","room-views-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output);
            PbrBaselineChecks.Capture(root,RoomReflectionChecks.FileName,output,false,forceLegacy:false);
            Console.WriteLine(output);
        }
        else GameplayFrameChecks.Run(root,args[1]=="on",levelFile:RoomReflectionChecks.FileName);
    }
    catch(Exception ex) {Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if (args is ["prepare-pbr-crate"] or ["validate-pbr-crate"])
{
    try
    {
        if (args[0] == "prepare-pbr-crate") PbrCrateChecks.Prepare(root);
        else PbrCrateChecks.Run(root);
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["cook-reflection-environment"])
{
    try { ReflectionCooker.Cook(root); }
    catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
    return;
}
if (args is ["validate-reflections"] or ["capture-reflection-room"] or ["validate-reflection-reference"] or ["profile-reflections"] or ["profile-reflections","--reverse"])
{
    try
    {
        if(args[0]=="validate-reflections") ReflectionChecks.Run(root);
        else if(args[0]=="profile-reflections") PbrPerformanceChecks.Run(root,args.Contains("--reverse"),reflections:true);
        else
        {
            string output=args[0]=="validate-reflection-reference" ? Path.Combine(root,"Tools/LevelAuthoring/References/PbrReflections") :
                Path.Combine(AppContext.BaseDirectory,"qa","reflections-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output);
            PbrBaselineChecks.Capture(root,PbrTestRoom.FileName,output,args[0]=="validate-reflection-reference",forceLegacy:false,reflections:true);
            Console.WriteLine("Reflection captures: "+output);
        }
    }
    catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
    return;
}
if (args is ["validate-pbr-lighting"] or ["validate-pbr-shadows"] or ["capture-pbr-room"] or ["validate-pbr-reference"])
{
    try
    {
        if (args[0] == "validate-pbr-lighting") PbrLightingChecks.Run();
        else if (args[0] == "validate-pbr-shadows") RenderPipelineChecks.Run(pbr:true);
        else if (args[0] == "validate-pbr-reference")
            PbrBaselineChecks.Capture(root,PbrTestRoom.FileName,Path.Combine(root,"Tools/LevelAuthoring/References/PbrDirect"),true,forceLegacy:false);
        else
        {
            string output = Path.Combine(AppContext.BaseDirectory,"qa","pbr-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output);
            PbrBaselineChecks.Capture(root,PbrTestRoom.FileName,output,false,forceLegacy:false);
            Console.WriteLine("PBR captures: " + output);
        }
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["refresh-pbr-normal-fixture"]) { PbrTestRoom.RefreshNormalFixture(root); return; }
if (args is ["validate-pbr-materials"])
{
    try { PbrMaterialChecks.Run(); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["profile-pbr-room"] or ["profile-pbr-room", "--reverse"] or ["benchmark-gameplay-pbr", "on" or "off"])
{
    try
    {
        if (args[0] == "profile-pbr-room") PbrPerformanceChecks.Run(root,args.Contains("--reverse"));
        else GameplayFrameChecks.Run(root, args[1] == "on", levelFile: PbrTestRoom.FileName);
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["pbr-baseline-one", var baselineFile, "capture" or "compare"])
{
    try { PbrBaselineChecks.Capture(root, baselineFile, Path.Combine(root, "Tools/LevelAuthoring/References/PbrStage0"), args[2] == "compare"); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["build-pbr-room"] or ["capture-pbr-baseline"] or ["validate-pbr-legacy"])
{
    try
    {
        if (args[0] == "build-pbr-room") PbrTestRoom.Build(root);
        else PbrBaselineChecks.Run(root, compare: args[0] == "validate-pbr-legacy");
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["build-probe-room"] or ["validate-probe-room"])
{
    try
    {
        if(args[0]=="build-probe-room") ProbeLightingRoom.Build(root);
        else LevelRenderingChecks.Run(root, indirectTest:true, probeTest:true);
    }
    catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if (args is ["benchmark-gameplay-probes", ..])
{
    try
    {
        if(args.Length<2 || args[1] is not ("on" or "off") || args.Skip(2).Any(a=>a is not ("--stress" or "--no-probes")))
            throw new ArgumentException("Use benchmark-gameplay-probes on|off [--stress] [--no-probes].");
        GameplayFrameChecks.Run(root,args[1]=="on",stress:args.Contains("--stress"),levelFile:ProbeLightingRoom.FileName,noProbes:args.Contains("--no-probes"));
    }
    catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    return;
}
if (args is ["validate-indirect-bake"])
{
    try { IndirectBakeChecks.Run(); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["validate-indirect-room"])
{
    try { LevelRenderingChecks.Run(root, indirectTest: true); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["build-indirect-room"] or ["inspect-indirect-room"] or ["bake-level", _])
{
    try
    {
        if (args[0] == "build-indirect-room") IndirectLightingRoom.Build(root);
        else if (args[0] == "inspect-indirect-room") IndirectLightingRoom.Inspect(root);
        else IndirectLightingRoom.Bake(root, Path.GetFullPath(args[1]));
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["benchmark-gameplay-indirect", "on" or "off"])
{
    try { GameplayFrameChecks.Run(root, args[1] == "on", levelFile: IndirectLightingRoom.FileName); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["build-six-room-emission-test"])
{
    try { SixRoomEmissionTest.Build(root); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["validate-emission"])
{
    try { EmissiveChecks.Run(root); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["validate-six-room-emission-test"])
{
    try { LevelRenderingChecks.Run(root, emissionTest: true); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["benchmark-gameplay-emission", "on" or "off"])
{
    try { GameplayFrameChecks.Run(root, args[1] == "on", levelFile: SixRoomEmissionTest.FileName); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["validate-collision-cache"] or ["benchmark-collision-load", "cached" or "source"] or ["cook-level-collisions", _])
{
    try
    {
        if (args[0] == "validate-collision-cache") CollisionCacheChecks.Run(root);
        else CollisionCacheChecks.Profile(root, args[0] == "cook-level-collisions" || args[1] == "cached",
            args[0] == "cook-level-collisions" ? Path.GetFullPath(args[1]) : null);
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["validate-visibility-streaming"])
{
    try { VisibilityStreamingChecks.Run(root); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["benchmark-gameplay-flashlight", ..])
{
    try
    {
        if (args.Length < 2 || args[1] is not ("on" or "off") ||
            args.Skip(2).Any(a => a is not ("--individual-uploads" or "--reference-visibility" or "--stress")))
            throw new ArgumentException("Use benchmark-gameplay-flashlight on|off [--individual-uploads] [--reference-visibility] [--stress].");
        GameplayFrameChecks.Run(root, args[1] == "on", args.Contains("--individual-uploads"), args.Contains("--reference-visibility"), args.Contains("--stress"));
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["benchmark-flashlight"])
{
    try { LevelRenderingChecks.Run(root, flashlightOnly: true, profileFlashlight: true); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["build-six-room-flashlight-test"] or ["validate-six-room-flashlight-test"])
{
    try
    {
        if (args[0].StartsWith("build-", StringComparison.Ordinal)) SixRoomFlashlightTest.Build(root);
        else SixRoomFlashlightTest.Validate(root);
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["benchmark-level-ready", var readyMode] && readyMode is "glb" or "cooked")
{
    try
    {
        CookedModelCache.Enabled = readyMode == "cooked";
        LevelRenderingChecks.Run(root, benchmarkOnly: true);
        Console.WriteLine("Fresh process, warm filesystem. Includes engine/window initialization, asset preparation/upload and GPU readback; excludes OS process launch, user input/playability checks and image encoding.");
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args.Length > 0 && args[0] is "validate-model-cache" or "validate-model-cache-load" or "benchmark-models")
{
    try
    {
        if (args is ["validate-model-cache"]) ModelCookingChecks.Run(root);
        else if (args is ["validate-model-cache-load", var path, var expected]) ModelCookingChecks.CheckChild(path, expected == "cooked");
        else if (args is ["benchmark-models", var mode] && mode is "cooked" or "glb") ModelCookingChecks.Benchmark(root, mode == "cooked");
        else throw new ArgumentException("Use validate-model-cache or benchmark-models cooked|glb.");
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args.Length == 1 && args[0] == "validate-lighting-state")
{
    try { LightingChecks.Run(); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args.Length == 2 && args[0] == "benchmark-textures" && args[1] is "cooked" or "rgba")
{
    TextureLoadingBenchmark.Run(root, args[1] == "cooked");
    return;
}
if (args.Length == 1 && args[0] == "validate-level-rendering")
{
    try { LevelRenderingChecks.Run(root); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args.Length == 1 && args[0] == "validate-rendering")
{
    try { RenderPipelineChecks.Run(); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args.Length == 1 && args[0] == "validate-engine-upgrades")
{
    EngineUpgradeChecks.Run(root);
    return;
}
if (args.Length > 0 && args[0] == "cook-assets")
{
    string folder = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "Game/Content/Models");
    var files = Directory.GetFiles(folder, "*.glb", SearchOption.AllDirectories).Order().ToArray();
    var timer = System.Diagnostics.Stopwatch.StartNew();
    int failures = 0;
    for (int i = 0; i < files.Length; i++)
    {
        try { TextureCooker.CookModel(files[i]); Console.WriteLine($"[{i + 1}/{files.Length}] {Path.GetFileName(files[i])}"); }
        catch (Exception ex) { failures++; Console.Error.WriteLine($"FAILED {files[i]}: {ex.Message}"); }
    }
    Console.WriteLine($"Cook: {timer.Elapsed.TotalSeconds:F1}s; {CookedModelCache.CookedModels} new model caches, {TextureCooker.CookedTextures} new textures, {TextureCooker.CacheHits} texture cache hits, {failures} failures. Source GLBs unchanged.");
    Environment.ExitCode = failures == 0 ? 0 : 1;
    return;
}
if (args.Length == 1 && args[0] == "validate-inventory-visuals")
{
    InventoryVisualChecks.Run(root);
    return;
}
if (args.Length > 0 && args[0] == "preview-inventory")
{
    Environment.SetEnvironmentVariable("HS2_RMLUI_NATIVE_PRESENTATION", args.Contains("--native") ? "1" : "0");
    int width = args.Contains("--small") ? 800 : 1280;
    int height = args.Contains("--small") ? 600 : 720;
    using var host = new Engine.Runtime.Hosting.EngineHost(width, height, "Inventory QA");
    using var preview = new InventoryPreview(root);
    host.Run(preview);
    return;
}
if (args.Length == 1 && args[0] == "build-inventory-thumbnails")
{
    InventoryThumbnails.Run(root);
    return;
}
if (args.Length == 1 && args[0] == "validate-redox-inventory")
{
    PersistenceInventoryChecks.Run(root);
    return;
}
if (args.Length == 1 && args[0] == "validate-inventory-input")
{
    try { InventoryInputChecks.Run(root); }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        Environment.ExitCode = 1;
    }
    return;
}
if (args.Length == 1 && args[0] == "validate-six-room-puzzles")
{
    PuzzleChecks.Run(root);
    return;
}
if (args.Length == 1 && args[0] == "add-six-room-puzzles")
{
    string path = Path.Combine(root, "Game/Content/Levels/sixRoomTest.json");
    var level = LevelIO.Load(path);
    SixRoomPuzzles.AddTo(root, level);
    LevelIO.Save(path, level);
    Console.WriteLine($"Added puzzles to existing layout: {level.Entities.Count} entities.");
    return;
}
if (args.Length == 1 && args[0] == "validate-collider-bounds")
{
    CollisionBoundsChecks.Run();
    return;
}
if (args.Length > 0 && args[0] == "build-six-room-test")
{
    string path = Path.Combine(root, "Game/Content/Levels/sixRoomTest.json");
    if (File.Exists(path) && !args.Contains("--replace-generated"))
        throw new IOException("Level already exists. Keep editor changes; generate into a fresh checkout to recreate the initial layout.");
    LevelIO.Save(path, SixRoomTest.Build());
    SixRoomTest.Validate(root, path);
    return;
}
if (args.Length == 1 && args[0] == "validate-six-room-test")
{
    SixRoomTest.Validate(root, Path.Combine(root, "Game/Content/Levels/sixRoomTest.json"));
    PuzzleChecks.Run(root);
    return;
}
foreach (string name in args)
{
    var model = GlbModelLoader.Load(Path.Combine(root, "Game/Content/Models/ViewModels", name + ".glb"));
    Vector3 min = new(float.MaxValue), max = new(float.MinValue);
    foreach (var part in model.Parts)
        foreach (var position in part.Positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
    Console.WriteLine($"{name}: min={min}, max={max}, size={max - min}, parts={model.Parts.Count}, textured={model.Parts.Count(p => p.BaseColorPng != null)}");
}
