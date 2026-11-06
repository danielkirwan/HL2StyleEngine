# Scheduling Trace Investigation

This is opt-in acceptance diagnostics, not an engine-system upgrade. Keep C#,
physics, quality, VSync and existing frame/memory gates unchanged. Run workloads
sequentially, with no concurrent builds, game or editor. Raw ETL stays local:
system scheduling traces can contain other process names and file paths.

## Capture

Build `Tools/LevelAuthoring/LevelAuthoring.csproj` and
`Tools/SchedulingProbe/SchedulingProbe.csproj` in Release first. The offline
analyzer references TraceEvent from the already installed local dotnet-gcdump
tool, not from the game. Override MSBuild `TraceEventDirectory` if that tool's
location changes. No new runtime package or native gameplay library is added.

Run the helper's `--self-test`, then:

```powershell
& ./Tools/LevelAuthoring/RunSchedulingInvestigation.ps1 -Long
```

Approve the Windows UAC prompt yourself. Only the recorder elevates; the actual
`profile-frame-pacing --reference --no-debug --long` process does not. Omit
`-Long` for the existing short full-detail control. This script does not build.

The uniquely named WPR recording captures switches, ready events, switch/ready
stacks, process/image identities, DPCs, interrupts and hard faults. It uses 64
1 MiB buffers and file mode. A nine-minute deadline bounds recording time. The
checked 2 GiB temporary ETL guard is best-effort: metadata can lag while WPR
writes, and the first captured merged ETL was 2.58 GiB. It is not a hard disk cap;
allow additional space for recording, final merge and transient disk use.
It stops only its own recording. A failed start never cancels another session.
Review `wpr-start.log`, `wpr-stop.log`, `error.txt`, `run.json` and
`scheduling.etl` under the printed `qa/scheduling-*` folder.

## Correlation

Version 5 opt-in frame reports retain existing timing fields and add the native
thread/process ID, QPC frequency and input/host/end timestamps. These use the
same Windows performance counter as the ETL, not an estimated FPS-to-time map.
Normal gameplay without the existing frame profiler does not collect them.

`SchedulingProbe <scheduling.etl> <frames.json>` writes
`scheduling-analysis.json` after the workload exits. It clips recorded switch-out
intervals to the slowest overall/gameplay host and physics frames, separating known ready time,
waiting until a ready event, and unclassified intervals. A switch-in process is
only the immediate successor, not proof it caused the delay. Running intervals
can contain interrupts. The automated report does not claim stack/driver cause.

Check lost events, frame coverage, phase, GC, Present, fixed ticks and the original
frame report before attribution. Open the ETL in WPA for unresolved stack,
interrupt, CPU migration or competing-thread questions. Do not infer an engine
wait from wall-minus-thread-CPU, or treat a trace with missing coverage as proof.

Traced runs have overhead and are diagnostic, not replacements for the original
untraced BAT/long controls. A clean repeat does not explain historical severe
stalls. Latest results and separate Gate L/B verdicts belong in
[the acceptance handover](LIGHTING_ACCEPTANCE_HANDOVER.md).

## First Capture (2026-11-06)

`qa/scheduling-20261106-120019-217`: approved recording completed and stopped;
35,683 matching switch-ins, zero lost events, selected frames covered. The long
full-detail workload did not reproduce the historical severe gameplay stalls.
The largest observed physics wait coincides with a loading-phase GC pause, not
the earlier no-GC incidents. [Detailed results](LIGHTING_ACCEPTANCE_RECHECK.md)
separate this diagnostic trace from untraced acceptance. Subsequent final BATs
and long controls pass. The user accepts Gate B with the historical stall kept
as an open monitored risk; no cause/fix is inferred. Reuse this bounded capture
workflow if a later audio regression run reproduces it. No always-on trace runs.

References: Microsoft's [CPU analysis](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/cpu-analysis)
describes running/ready/wait states and WPA precise CPU analysis;
[WPR/WPA troubleshooting](https://learn.microsoft.com/en-us/troubleshoot/windows-server/support-tools/support-tools-xperf-wpa-wpr)
describes the capture workflow. The installed WPR schema validates the custom
profile; the installed TraceEvent assembly supplies the ETL parser.
