# Clean Output aspect correction — 2026-11-09

Cause: the VR desktop mirror and Clean Output rendered the headset eye projection directly into a differently shaped flat-screen target. At 1920 × 1080, unequal horizontal and vertical pixel scale stretched the robot sideways and made it look short. GPU letterboxing preserved that already-distorted image; it could not correct the camera projection.

The preview now uses a centered monoscopic camera at the tracked head position, following the same locomotion origin. Its projection matches the actual render-target aspect ratio. Excess vertical/horizontal headset coverage is cropped rather than stretched, and the headset eye offsets/cant are not applied to the flat-screen camera. The headset's original left/right projection matrices and eye transforms are unchanged. This corrects both the regular desktop VR preview and Clean Output. The standalone non-VR camera already used the proper target aspect and is unchanged.

There is still only one mirror render. When Clean Output is active, the main preview remains disabled. Output refresh, resolution, display selection and minimized rendering behavior are unchanged. The correction can show less of the top/bottom compared with the previous stretched image; back up or adjust framing when needed rather than stretching the image.

Validation: Release publish succeeded and 34 focused Clean Output checks passed. These include equal horizontal/vertical projected pixel scale at 1920 × 1080, 1280 × 1024, 1080 × 1920 and 3840 × 2160; centered projection/depth preservation; unchanged headset matrices; camera/head-center placement; and existing actual GPU blit, resize, fallback and settings checks. No full suite or headset integration test was run. Verify proportions in the gaming-PC preview and OBS.

Install: close both VR applications and extract J5Studio-VR-Clean-Output-Aspect-Fix-update.zip over the existing portable VR folder, keeping its subfolders. It is cumulative since Johnny5VR-0.9.0-cumulative-update-2026-11-08.zip and includes Clean Output and previous Follow/menu fixes. Config, Settings, models and saved calibration are not overwritten.
