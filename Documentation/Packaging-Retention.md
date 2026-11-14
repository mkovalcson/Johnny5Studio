# Packaging and release retention

Finished packages live in Releases/Desktop/<version> and Releases/VR/<version>. Desktop/Current is a rolling convenience deployment. Development shortcuts still use development builds. Each release retains its own Config snapshot.

Keep the two newest numbered release versions per application in Releases. Older versions move intact into Archive/RetiredReleases; they are never automatically deleted. Keep the two newest explicitly marked viewer/editor publish versions. Unmarked folders and editor-check are protected. Published builds containing Config or Settings are not automatically removed. Only explicitly marked temporary stage/verification trees are disposable as whole trees.

VR assembly uses packaging/staging and removes its own staging tree after success, leaving one runnable finished deployment beside its ZIP. Manual verify_portable.py removes its own fresh extraction after success by default; --keep-extracted retains it. No archive verification is run automatically by retention maintenance. Explicitly marked temporary stage/verification folders older than seven days can be cleaned on the next successful packaging run.

Policy settings are in tools/retention-policy.json. Preview maintenance actions with `python tools/maintain_artifacts.py`; use --apply to perform them. Finished VR/Desktop package scripts run maintenance after success. Nothing under CAD or the shared Config is a retention target. Archived Config snapshots are kept intact.
