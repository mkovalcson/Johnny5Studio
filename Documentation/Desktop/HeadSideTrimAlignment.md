# Head side trim alignment — 2026-11-03

The gray strips directly below the blue whip-antenna housing and the opposite upper side panel projected about 1.14 mm beyond their mating panels. These are separate CAD solids 43 and 44 in SimplifiedHead2.step; translating the entire upper head cannot correct both sides.

Both managed robot models now move only those strips 1.10 mm inward along CAD X (positive X on the whip side, negative X on the opposite side). Outer edges align within 0.06 mm. A small remaining clearance avoids coincident colored surfaces and flickering at the overlapping housing seam. Heights and front/back placement are unchanged.

Updated assets in each of Config/Models/Robots/Johnny5Head and Johnny5FullBody:
- Meshes/SimplifiedHead2/static_737373.stl (144 + 38 triangles translated)
- Meshes/SimplifiedHead2/static_737373_collision_full.stl (203 + 44 triangles translated)

All remaining triangles, normals, URDF origins, joints, eyes, fasteners and prior rear-cover correction are unchanged. Original STEP files are preserved. The editor must reload the model (or restart) to discard the previously cached mesh; this asset-only correction requires no executable update.

Original mesh files and the measured change report are in change-backups/head-side-trim-alignment-20261103. tools/extract_head_trims.py extracts reference solids from the preserved STEP; tools/align_head_side_trims.py installs the correction once, with backups; tools/verify_head_side_trims.py verifies exact displacement and preservation against those backups. Run these using tools/cad-bolts-env/Scripts/python.exe. tools/render_head_alignment.py produces inspection views in head-alignment-previews.
