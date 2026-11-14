# Working on J5 Studio

Read [DEVELOPMENT.md](DEVELOPMENT.md) before building or changing code. Check
[Documentation/README.md](Documentation/README.md) for architecture and feature notes.
Keep changes to shared control names, pose math, formats and configuration in Shared when practical.
Check both editors for duplicate integration code. Keep the VR Player and Editor bridge compatible.
Keep Help indexes and shared Help pages current. Use the disposable test Config, never personal work.
Do not mutate hardware or a running editor as part of automated checks. Review changes before
updating expected baselines, and record what was checked and what needs headset validation.

For local editor automation see [Editor API](ServoAnimator/ServoAnimator/Help/EditorApi.md)
and [MCP setup](Johnny5-Editor-MCP/README.md). Read editor state before editing and use the
current context token; don't rewrite project files behind an open editor.
