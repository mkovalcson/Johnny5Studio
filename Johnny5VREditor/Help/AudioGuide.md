# Trim and replace speech

Stop timeline playback completely before opening Help.

## Trim or replace recorded speech

1. Right-click the primary or added audio handle and open Audio timing / Skip to / End at.
2. Enter Skip to in source-audio seconds; optionally scrub commands before it. Commit locks the value; click Commit again to edit.
3. Set End at similarly to shorten the tail. Hidden commands can return when bounds are relaxed; Reset trims restores the original interval.
4. For externally processed speech, choose Special Audio Replace, select the new audio and optional Command Update JSON.
5. Clear active trims before re-timing, review the effects, apply, preview and save.

Expected result: ordinary trims remain reversible; Special Audio Replace can re-time all following commands to match a longer processed track. See its reference page before applying it to a busy sequence.

For saving and export choices, see [Save, export and reuse your work](Saving.md).
