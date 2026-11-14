# VR Player system requirements (estimated)

These are planning estimates for **headset use** in J5 Studio VR Player. Headset resolution and refresh rate, SteamVR render scale, scene complexity and concurrent apps can materially change performance.

## Software and headset

- Use a supported **64-bit Windows 11** release and a **Direct3D 11 feature level 11_0** GPU with current drivers. The self-contained portable package includes its .NET runtime; a framework-dependent build needs the .NET 10 Windows Desktop Runtime (x64).
- Install **SteamVR** and any drivers or utility required by the headset. Use a SteamVR-compatible PC headset with the graphics output, USB ports, controllers and tracking hardware its manufacturer requires. Desktop preview can run without a headset.
- Use the higher of the headset manufacturer's requirements and the workload estimate below. Valve's SteamVR requirements describe SteamVR itself, not a Johnny 5 scene. See https://store.steampowered.com/app/250820/SteamVR/ and, for Beyond, https://store.bigscreenvr.com/products/bigscreen-beyond-2.

## Hardware planning tiers

- **Starting point for standalone playback with a moderate-resolution headset:** modern 6-core CPU, **16 GB RAM**, and a dedicated **RTX 3060-class GPU** or comparable headset-supported GPU with **8 GB VRAM or more**. Start with a simple backdrop and conservative render scale. This tier is not a high-resolution headset recommendation.
- **Recommended for high-resolution VR, live VR Editor, or Follow:** modern 8-core CPU or strong equivalent, **32 GB RAM**, and an **RTX 4080-class GPU or comparable headset-supported GPU with 16 GB VRAM**. A faster GPU gives more room for high render scale, complex scenes and Clean Output capture; editor work can still be CPU-limited.
- **Storage:** start with **15 GB free on an SSD** for the Player, optional matching VR Editor, and working data. Reserve extra space for Config models and media, diagnostic captures and OBS recordings.

With a **Bigscreen Beyond 2/2e**, verify the exact GPU and port configuration against Bigscreen's requirements before buying. A GPU that can open the desktop viewer may not initialize the headset. Try your actual scene at your target refresh rate; at **75 Hz** the frame budget is about **13.3 ms**. Inspect both CPU and GPU frame times in SteamVR rather than inferring performance from average GPU utilization. Check with the VR Editor, Follow, and capture running if those are part of your workflow.

See [Desktop Editor system requirements](DesktopSystemRequirements.md) for editing without a headset.
