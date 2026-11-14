# Test fixtures

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Initialize-Config.ps1 -Fixture`
from the repository root. This creates a disposable `TestFixtures/Work/Config` from the tracked
template, including the bundled robot and scenery models, TipToe scene and sequence. The
script refuses to overwrite an existing copy. Do not run integration checks against live work.

The shared check project folders retain their baseline files. See [development setup](../DEVELOPMENT.md).
