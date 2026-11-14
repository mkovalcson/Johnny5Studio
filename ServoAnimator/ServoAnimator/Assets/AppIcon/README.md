# Current application icon: option C

Approved simplified teal rounded square with large white J5 lettering and a yellow play triangle. The spline line and diamond have been removed. The source uses enlarged lettering and a tight gap above the triangle for small-size readability.

- Source: `animation-editor-c-source.png`
- Windows resource: `animation-editor.ico`
- Repackage with `build-icon.py` (Pillow); includes 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 pixel frames.
- The existing project ApplicationIcon and WPF window icon resource both reference this ICO.
- Generated using the built-in image generation tool. Prompt: isolate approved option C, large bold white J5 above a broad yellow play triangle on teal, transparent outside the rounded square, flat colors, no other details. A corrective pass removed unwanted background transparency patches.

A second clean-up (2026-10-08) removed the remaining patches: inside the rounded square every pixel is now fully opaque, and the background is one even teal (3, 145, 150), keeping the white lettering's soft edges. Only the rounded square's anti-aliased rim and the corners outside it stay transparent.

Previous artwork is retained below for provenance; it is no longer selected by the packaging script.

# J5 Animation Editor application icon

Current design (chosen 2026-10-01): **J5 monogram** — bold white "J5" on a teal tile, an amber
animation spline ending in a keyframe diamond, and a simple amber play triangle sitting on the
spline. It was designed to stay recognisable at taskbar and title-bar sizes (16–32 px).

`animation-editor-j5-source.png` is the 1024 px artwork, used for every frame.
`animation-editor.ico` contains 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 px frames.
The project embeds the icon in the Windows executable and as a WPF resource.
App startup applies it to windows without an explicit icon.

Run `build-icon.py` with Python and Pillow to regenerate the ICO.
The script only resizes and packages the approved artwork.
Rebuild/redeploy to update executable icons. Existing Windows shortcut/taskbar
caches may retain an older image until the shortcut is refreshed or re-pinned.

## Previous design (kept)

Concept B (Motion Studio): metallic robot, cyan eyes, amber animation spline/keyframes.
`animation-editor-source.png` is its artwork and `animation-editor-small-source.png` its
simplified eyes/play artwork for 16–32 px. To rebuild that mixed icon, set `SMALL_SOURCE` and
`LARGE_SOURCE` in `build-icon.py` back to those two files.
