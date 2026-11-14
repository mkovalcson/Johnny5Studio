# Optional URDF weathering

Workshop Used (level 2) has a head/neck trial recipe in
UrdfScene.Weathering.Workshop.cs. Exact asset families select painted housing,
flap, shell, brass, rubber or protected treatments, shared by both robot models.
Separated contact zones weight irregular chips along exposed edges; curved rims
also use the projected outside envelope. Interior seams receive darkening, not
silver chips. Paint has a charcoal/navy diffuse floor because the CAD black is
near zero reflectance. Reduced specular energy gives a matte finish without
changing global lighting or clean appearance. Optics, blackout caps, mouth
electronics and piston shafts are protected. Preparation remains asynchronous
and cached. The approved head recipes remain unchanged.
--workshop-head captures actual DX11 before/after images with the same camera
and lighting, verifies restrained paint coverage, protected optics and exact
Clean restoration. The before image recreates the former level-2 materials
on the same mapped geometry; it is not an AI-generated concept.

Workshop refinement: flap rims have denser chips; black paint uses a lower
diffuse floor. Only vent_left_fin1..5.stl and vent_right_fin1..5.stl receive the
stronger edge chips. Both the fins and whip-antenna housing use the same navy
floor (RGB 50/58/109), halfway between the earlier housing (35/43/100) and faded
fins (65/73/118). Light Wear is unchanged. The actual animated blue fins have a separate
visible convex-edge mask for their exposed lips; the silver slotted rear cover
uses the shell treatment. Other panels retain the protected-interior rule.
The extra edge mask is allocated only for blue fin assets, reusing the original
edge bake. --workshop-head verifies all ten named fins and the shared paint shade.

Body Light Wear/Workshop Used select BodyPaint, BodyMetal, BodyBrass,
BodyRubber or BodyProtected from body link/mesh identity, material names and
diffuse color. Both primitive and imported geometry use the same sheltered-edge
masks as the head. Light Wear scales the treatment to .28, Workshop Used to 1:
paint chips/scratches are localized at contacts; metal receives subtle patina
with polished contact scuffs; brass tarnishes without silver paint chips.
Bellows, hoses, tread bands/pads stay black, while electronics, fasteners and
polished shafts retain their original diffuse color. Metal tread plates receive
edge masks even though the older generic tread classifier called them rubber.
Head Light Wear/Workshop and Outdoor Aged use their existing recipes. Gold is
independent. --body-weathering checks body sections, increasing wear, protection,
UI heartbeats, cached switching and exact Clean restoration. --dx11 captures
Clean/Light/Workshop full-body and shoulder, hand and chest detail views.

URDF Configuration > Renderer and appearance > Weathering / distress offers
0 Clean (default), 1 Light wear, 2 Workshop used and 3 Outdoor aged.
Changes preview live; Save Default persists Rendering.WeatheringLevel in
URDFconfig.json. Back restores the opening level. The level is shared across
WPF and Direct3D 11, independent of the renderer-specific lighting/tone profiles.
Reset this renderer leaves it alone; configuration defaults restore Clean.

UrdfScene.Weathering.cs registers opaque imported and built-in mesh surfaces and lazily assigns
deterministic procedural color textures plus reduced specular intensity.
Box projection uses a six-face texture atlas in each part's local coordinates.
Disconnected solids within one CAD material mesh get separate atlas charts;
otherwise an edge from one solid can incorrectly appear on another. Charts are
packed in a grid with adaptive resolution to bound texture size. Each chart has
its own bounds, projection and nearest-surface depth raster.
Black/blue and other strongly colored paint receives silver edge chips and
scratches. Bare metal receives mottled warm-gray patina; brass receives brown
tarnish and some green patina at level 3. Outdoor aged adds localized rusty
brown patches around exposed edges. Rubber receives mild dulling only, never
silver chips or rust. Stainless fastener assets do not rust. Material color and
explicit rubber/fastener mesh names distinguish these visual treatments; the
source CAD does not identify every actual metal alloy. Optics/irises/pupils,
animated lighting, screen materials and laser effects are excluded.

Mapped geometry retains the exact original positions, normals and triangles.
STL collections are shared with frozen originals; only UV coordinates are added.
Mapping is cached per scene; materials/textures are shared for matching meshes and colors
and generated once per level in a scene. Level 0 restores original
mesh/material references. No URDF, STL or extra image assets are written.

Live views call RequestWeathering. Frozen geometry and value-only transforms,
colors and bolt positions are captured on the UI thread; a bounded background
worker prepares maps/materials. Only the final model assignments run on the UI
dispatcher. Each scene caches all three completed finishes and UV geometry.
Generation tokens/cancellation prevent stale jobs overwriting newer selections;
Clean and Gold bypass preparation. A progress label in URDF Configuration reads
the worker status. Explicit bitmap/frame exports pump the dispatcher while
waiting for the selected finish, so they never capture the previous appearance.
Hidden/unloaded RobotHeadViews defer preparation and cancel pending work; visibility
restoration requests the remembered level. This avoids duplicate work for the
inactive docked/undocked preview. Explicit captures still request the finish even
when the view has not been shown yet.

The texture loop hoists per-pixel array allocations and uses a linear-color
lookup table instead of constructing Colors for every texel during averaging.
Atlas dimensions are precomputed. Rubber/stainless skip unused edge/depth masks
and share equal color/layout finishes. Prepared materials and geometry are frozen
before being published from the worker. --weathering-preparation measures first
preparation, cached switching and UI heartbeats on both models, and checks
cancellation, immediate Clean/Gold and material restoration.

Edge masks are baked from welded mesh boundary edges and convex geometric creases
over 40 degrees, projected onto each adjacent atlas face. Concave corners are
protected. A nearest-surface depth raster rejects hidden rear/internal edges,
preventing their outlines from appearing through the exterior. Contact wear is
further restricted to the convex outside
envelope of each chart, so enclosed grooves and re-entrant notches are sheltered.
Coplanar STL triangle
diagonals and gentle cylindrical facets do not become chipped edges. Masks and
mapped meshes are cached across levels; common noise/scratch fields are baked
once. Paint chips use that geometry mask rather than the border of a UV tile.
Fine scratches are confined to exposed edge regions; increasing
levels strengthens the same marks rather than randomly replacing the finish.

Built-in primitives are now included (including shoulders, finger segments and
finger disks); indexed triangles may be expanded for UV mapping while retaining
their exact triangle positions/normals. Bare metal uses darker patina and polished
edge wear for contrast. Stainless and named bolt/screw visuals in the same URDF link supply rust
streak origins: positions are transformed into each panel's coordinates, accepted
only near its visible surface, and extended downward with a fade. Rust stains the
surrounding material, not the stainless bolt itself.

Rendering.RobotFinish selects Original or GoldJohnny. Gold replaces opaque body
materials, including rubber and primitives, with a frozen bronze-gold material:
#AB7936 / RGB 171,121,54, about 8% lighter than the user's #9E7032 swatch. Standard
neutral white specular brush (alpha 130) and power 32 retain light-driven highlights;
the swatch's bright vertical bands are not painted onto the model. Original-color
neck cylinders keep their existing neck_gold material.
The left/right eye tubes, fixed support brackets, top/bottom gimbal bars, gimbal
frames/spacers and their pivot screws are gold. Lenses, irises, front eye trim,
piston shafts, black neck/waist
bellows, black tread pads, mouth LEDs and their green PCB, and screen/effect materials
are protected. Structural eye parts use an explicit allowlist of mesh filenames
so the internal eye tube blackout caps remain unchanged.
Gold pauses weathering without changing its saved level; restoration reuses the
original materials/geometry and collision highlight bookkeeping.
Rendering.DefaultAccessory selects None (including older files missing this
setting), Toolbox or Laser. RobotHeadView applies it when a model/default changes;
MainWindow evaluates accessory timeline commands using each view's own default.
Explicit Toolbox Swap overrides None; Hide Laser remains authoritative when set.

Tread animation phases reuse the first phase's UV coordinates when topology
matches, so scratches follow the tread shoes. Phase maps are prepared on selection,
with no new texture generation in the animation loop. Final clean geometry and
materials are captured after tread/other scene initializers finish. Active
collision highlights remain red; clearing them restores the selected finish.

Opaque Bgr32 image maps stay in DX11's opaque rendering pass. Unknown/alpha
images retain transparent handling, including the existing clear eye lenses.
Generated weathering maps use sRGB texture decoding in DX11, matching the linear
ScRGB colors used for clean solid materials. Their ambient color is the average
linear map reflectance: Helix's Phong ambient term does not sample the diffuse
map. Using a white ambient multiplier here washes out the model. This metadata
applies only to generated weathering maps; video, LCD and lens maps are unchanged.
No renderer-specific procedural shader or per-frame surface generation is used.

Run ServoAnimator.Checks --weathering for UI, persistence, legacy defaults,
both models, exact restoration, protected surfaces, tread UVs, geometry reuse,
collision highlight restoration, and WPF comparison PNGs in weathering-previews.
--dx11 verifies all three distressed levels on the physical GPU, visibly
changed output, opaque maps and reuse of GPU geometry between levels. Its
screenshots are in DevelopmentArtifacts/Previews/renderer-previews/Dx11-weathering-1/2/3.png.

These are procedural finishes, not the AI concept images. Local box projection
can have texture-direction seams on curved surfaces; the initial version does
not use CAD curvature surfaces, artist-authored UVs, normal maps or physically based
roughness maps. Those can refine wear placement later without changing the level
selector or original model assets.
