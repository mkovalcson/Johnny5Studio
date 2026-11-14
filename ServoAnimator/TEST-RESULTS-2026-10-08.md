# J5 Animation Editor & Player 2.0.0 — release verification

Release build and self-contained Windows x64 publish succeeded on October 8, 2026.
The assembly product name is J5 Animation Editor & Player, version 2.0.0.
Two existing CS0649 vent-center warnings remain; no new compiler errors occurred.

| Check | Passing assertions |
| --- | ---: |
| General editor regression | 6697 |
| URDF presentation | 253 |
| Hardware timeline and visual-only group hiding | 32 |
| Configuration discovery, naming and docking | 16 |
| Status popup contrast, wrapping and interaction | 30 |
| Help structure, links, search and rendering | 109 |
| Packaged relative models, scenes and images | 11 |

Package inspection confirmed 9 relative model references and 682 mesh references
resolve to bundled files. Full-body robot, backdrop and retained scene images
load directly from the staged package. No Sylogist scene or image is included.
The ZIP retains the other libraries; the repository Config contains only the
required configuration files and hardware support utilities.

Checks instantiate unshown controls or load model assets; no physical robot
output, camera capture or running-editor document mutation was performed.
Live configuration files are retained and their hashes are checked before and
after the final release/repository operations. Repository originals are
preserved in a separate release-backups folder before replacement.
