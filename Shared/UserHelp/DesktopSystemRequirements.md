# Desktop Editor system requirements (estimated)

These are planning estimates for J5 Studio Desktop. They are not tested minimums or guarantees for every robot model or project.

## Software

- A supported **64-bit Windows 11** release is the recommended baseline. Both editors target .NET 10 for Windows. For a framework-dependent installation, install the **.NET 10 Windows Desktop Runtime (x64)**; a self-contained package already includes its runtime. See Microsoft's Windows .NET support information at https://learn.microsoft.com/dotnet/core/install/windows.
- Use an up-to-date graphics driver. **Direct3D 11** support is needed for the optional D3D11 viewport; the editor also offers a WPF viewport. SteamVR and a headset are not required.

## Hardware planning tiers

- **Basic editing and light preview:** recent 4-core desktop CPU, **8 GB RAM**, modern integrated graphics with hardware acceleration, and an SSD. Aim for 1920 × 1080 desktop space. This tier is for timelines, audio, and lighter scenes.
- **Recommended for full-body 3D and regular editing:** modern 6-core CPU, **16 GB RAM**, and a dedicated GPU with **4–6 GB VRAM**. Use **32 GB RAM** if you regularly combine detailed models, large scenes, video and other applications.
- **Storage:** allow roughly **10 GB of free SSD space** as a starting budget for installation and working files. Large Config libraries, project media, exports and backups need additional space.

A webcam is needed only for face tracking; real robot control needs the appropriate interfaces and devices. Start with a 30 fps webcam capture target and check the processing readout before moving to 60 fps. If a 3D scene is slow, compare WPF and D3D11 on the same project at the same window size and reduce anti-aliasing or backdrop complexity before upgrading.

See [VR Player system requirements](VrSystemRequirements.md) for headset use.
