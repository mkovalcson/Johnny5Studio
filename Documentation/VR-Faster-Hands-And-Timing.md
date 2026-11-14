# Faster Follow hands and timing review — 2026-11-08

## Hand response

Follow-mode wrist pitch/roll, thumb curl/tip, and upper/lower finger spread, base curl, tip curl and end curl now have twice their previous angular speed limits on both sides. The existing shoulder rotation/elbow 3x boost is preserved. Other body limits, calibration, sensor mapping and timeline playback speeds are unchanged. This changes the maximum rate of following, not the range of motion or controller sensing frequency.

## Timing evidence

Source: E:/bridge-timing-20261108-130414.csv, compared with E:/bridge-timing-20261108-122747.csv. The old log did not mark Follow explicitly, so its steady hidden-editor interval is a comparison sample rather than a controlled identical workload.

Old steady sample: 114 rows, 12:29:10–12:31:04. New steady Follow sample: 172 rows marked editor-vr-follow excluding the initial 13:05:38 transition. Mean rates below use per-row values; per-operation durations are weighted by operation counts.

| Metric | Earlier steady editor | New steady Follow |
|---|---:|---:|
| Headset frame rate | 74.991 fps | 74.997 fps |
| Applied robot scene updates per logged second | 20.35 | 23.32 |
| Temporary managed allocation per logged second | 329.96 MB | 73.39 MB |
| Apply scene duration per update | about 1.42 ms | about 0.30 ms |
| Full generation-2 collections per logged second | 1.00 | 0.076 |
| Late render intervals (total sample) | 2 | 0 |
| SteamVR reported dropped/reprojected frames | 0 / 1 | 0 / 0 |

The new steady Follow period rebuilds zero dynamic vertices and allocates zero replacement dynamic GPU buffers: the geometry reuse is working. Allocation is about 78% lower, while scene application is about 79% faster. Headset FPS was already at its 75 Hz ceiling; improvement appears in lower work, less memory churn and steadier intervals.

## Remaining opportunities

1. Scene updates still average 23.3 Hz against the 30 Hz target. New Follow scene request/capture/transfer averages about 16.6 ms; decode about 3.3 ms. The round trip peaks at 66.9 ms even when headset frames remain smooth. Scheduling/consumer waits, capture and decode are the next investigation area. The current log does not separately measure capture, encoding, consumer waiting or pacing delay, so it cannot establish which accounts for the missing update rate. Add those measurements before choosing the next bridge optimization.
2. At 13:05:38, entering Follow produces a 232 ms interval, with UpdatePose reaching 227 ms. Scene application is below 1 ms and GC pause 0.6 ms in that row. This points toward VR input/overlay/Follow initialization rather than scene upload or garbage collection. Lazy glove LCD/overlay creation is a candidate; the current UpdatePose measurement is too broad to prove it. Prewarming and finer phase timings would help.
3. Initial editor scene loading at 13:05:02 still shows a 190 ms application and a 1.17 second background decode. These are startup costs rather than steady Follow. Moving initial asset preparation off the render thread is a separate possible improvement.
4. Remaining Follow allocation averages 73 MB/s; packet decode, scene bookkeeping and per-frame glove/UI data are candidates for further reuse. The CSV does not attribute allocations by source. Lowering them further is less urgent than improving update cadence and the initial stall.

SteamVR WaitPoses is mostly waiting for the next headset frame, not a processing bottleneck. The log gives no evidence of sustained GPU/CPU reprojection or dropped frames during steady Follow. No additional performance changes were made with this hand-speed update.

## Install and validation

Close VR Player and VR Editor and extract J5Studio-VR-Faster-Hands-update.zip into the existing portable VR folder, replacing files and keeping Editor/Shaders subfolders. This cumulative update includes the preceding Follow performance, scene/status preferences, glove LCDs and other changes since the November 8 cumulative package. Config, calibration, models and saved settings are preserved. Both applications contain the updated shared pose library. The regular Desktop Editor is unchanged.
