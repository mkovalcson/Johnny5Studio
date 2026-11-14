# Recessed head fasteners

Current added total: **114 screws/bolts per robot model**: 90 recessed socket
heads, 12 button socket heads and 12 black flat-head flap screws.

The four eye flaps now have copies of the existing tiny flat-head vent-fin
screw: four on each upper flap and two on each lower flap. The original screw
was extracted unchanged from the silver CAD mesh beside the left top vent fin
(CAD cylinder 1453 identifies its seat), reoriented with its top face at Z=0,
and colored black. Its head radius is about 1.2573 mm and total length 3.175 mm.
Each flap seat was measured from the runtime mesh's circular outer boundary
(1.5113 mm radius). The top face sits flush and the shank points inward, with
the original screw size retained. Lower flap normals include their small CAD
tilts. Visuals belong to each flap's existing articulated link, and the shared
asset is a normal URDF mesh dependency included in model exports.

`tools/install_flap_flat_screws.py` installs them with duplicate protection.
`change-backups/flap-flat-screws-20261103/` preserves both preceding URDFs and
a placement/source-hash manifest. No original meshes, previous hardware,
joint definitions or collision geometry change. `--flap-screws` verifies both
robots, placement/size/flushness, black material, parent links and preservation,
and writes close-up renders to `head-fastener-previews/`.

Eight front mounting holes and four rear-cover flange holes now contain the
supplied `SBHCSCREW 0.086-56x0.5625-HX-N` domed stainless steel screw. The front
2.286 mm diameter holes use the original screw dimensions; the rear 3.264 mm
diameter holes use uniformly scaled screws (about 1.428×). The underside of each
head sits on the outer panel surface; the dome and hex socket face outward.
These are surface-mounted button heads rather than flush recessed socket heads.
Both models reference `Meshes/Fasteners/stainless_button_socket_head.step.stl`,
with crease-preserving smooth shading. All 90 earlier bolts and existing model
geometry, joints, lenses and collision shapes remain unchanged. The mesh is a
normal URDF dependency and travels with model export packages.

`tools/install_button_head_bolts.py` measures the eight front hole rings and
four rear hole rings directly in the runtime meshes, fits circle centers/radii,
and respects each host origin including the rear-cover vertical correction.
Front seats correspond to SimplifiedHead2 CAD cylinders
1232/1233/1234/1235/1241/1242/1243/1244; rear seats to rearCoverColor cylinders 0–3.
`change-backups/button-head-fasteners-20261103/` preserves the supplied STEP,
original tessellation, both immediately preceding URDFs and placement manifest.
The render mesh is translated so its under-head plane is local X=0; its shank
extends along +X and its dome faces -X. Run
`ServoAnimator.Checks --button-head-fasteners` for placement, fit, shading,
preservation checks and front/rear close-ups in `head-fastener-previews/`.

The preceding four fasteners fill the
gold detail plate beside the whip antenna. CAD cylinders 1290/1292/1294/1296 in
SimplifiedHead2 define 1.651 mm radius, 1.905 mm deep counterbores in
`static_e7c863.stl` (`static_05`). The bolt heads use the same fitted dimensions
as the smaller black detail-plate bolts and sit flush on the gold surface.
Both models reuse the supplied stainless socket-head mesh and existing material.
No housing meshes, collisions, joints, older bolts or spherical lenses change.

`tools/audit_gold_plate_bolts.py` finds the four analytic seats and checks their
inward cylindrical STL surfaces. `tools/install_gold_plate_bolts.py` installs
them with duplicate protection. Backups of the immediately preceding models and
manifest are under `change-backups/gold-whip-plate-fasteners-20261103/`; the main
manifest marks the four additions with `goldWhipPlate: true`. Run
`ServoAnimator.Checks --gold-plate-fasteners` for the fit/preservation checks and
focused previews in `head-fastener-previews/*-gold-whip-plate.png`.

The original additions are documented below.

The managed Johnny5Head and Johnny5FullBody URDFs originally included 86 stainless socket-head bolt visuals derived from the supplied `HX-SHCS 0.086-56x0.125x0.125-N` STEP file. The original 33 are supplemented by 53 smaller bolts in the black side and underside detail plates. Those recesses have a 1.651 mm radius and 1.905 mm depth; their fitted heads have a 1.4859 mm radius and approximately 1.8255 mm depth. Fitted sizes retain the supplied socket-head shape. Each visual belongs to the surrounding part's existing URDF link; four fasteners belong to the moving eye assemblies and the remainder to the fixed head link.

The analytic STEP cylinder audit requires a complete circular recess, a concentric smaller bore meeting one end, and a matching inward cylindrical surface in the runtime STL. Rounded corners, plain through-holes, ambiguous features, and existing hardware are excluded. Nine occupied or already modeled hardware seats remain unchanged. Rear-cover CAD orientation was examined separately; its rounded corners were not treated as screw counterbores.

Both models share identical copies of `Meshes/Fasteners/stainless_socket_head.step.stl`. It contains 1,661 triangles, tessellated at 0.04 mm linear and 0.3 radian angular deflection, and uses the renderer's existing crease-preserving smooth shading. Repeated sizes share geometry through the existing URDF mesh cache. The mesh is referenced normally by the URDF and is therefore included in model export packages. No source shell mesh, existing collision geometry, joints, or rear-cover correction was altered.

`change-backups/head-fasteners-20261103/` preserves both prior URDFs, the supplied STEP, the render mesh and `fastener-placement-manifest.json`. The manifest records the original STEP hash, render mesh hash, each bolt's parent visual/link, CAD cylinder, recess dimensions, fitted scale, outer rim and normal. The external supplied STEP remains unchanged.

The smaller detail-plate recesses are installed by `tools/install_detail_plate_bolts.py`, using their analytic CAD counterbores and matching runtime mesh surfaces. `change-backups/detail-plate-fasteners-20261103/` preserves both models and the manifest as they were before these 53 additions, plus the new seat audit. The main placement manifest includes all 86 bolts per model and marks these additions with `detailPlate: true`.

Workspace-only CAD tools use an isolated environment under `tools/cad-bolts-env` with `cadquery-ocp`, `numpy` and `scipy`. The audit scripts are `inspect_head_bolts.py`, `align_rear_cover_cad.py`, `match_head_recesses.py`, `select_head_bolt_seats.py`, and `install_head_bolts.py`. The installer rejects duplicate installation. Analysis JSON files remain beside those tools; the canonical installed-placement record is the archived manifest.

Run `ServoAnimator.Checks --head-fasteners` from the ServoAnimator workspace to check source preservation, existing-model preservation, bolt counts, mesh sharing, parent links, flushness and head clearance. `--eye-surfaces` also checks the new bolts' smooth shading, and `--dx11` exercises both anti-aliasing modes, animation, disassembly and video with the installed full-body model.
