# Grouped sequence insertion and overlap review

Desktop and VR Editors share the insertion planner and review window. Inserting a Library Sequence or using Insert commands from JSON gives all accepted commands one new group, independent of groups saved in the source and independent of earlier insertions.

The insertion span starts at the captured timeline cursor and ends at the source's last command or declared duration, extended for the duration of readable audio Play clips. Both endpoints are inclusive. The dialog displays the actual start/end times. It appears when any existing commands target controls present in the inserted sequence anywhere in that interval, even if their time points differ.

For each affected control, the window lists original and inserted command counts and offers:

1. Keep original: retain original commands and omit inserted commands for this control.
2. Use inserted: remove original commands for this control within the insertion span and accept inserted commands.
3. Merge (initial selection): retain both sets except that inserted commands replace original commands at identical millisecond time keys.

Controls with no overlap are inserted normally. Commands outside the insertion interval are retained. Ganged commands are compared by member controls; if choices differ between members, commands are split as needed to retain unselected members. Existing group identities are retained on surviving original commands. Cancel changes neither commands nor Undo history, and library audio is copied only after accepting the review.

Accepting the insertion creates one Undo step. Save the sequence normally. The feature does not create a separate sequence file or change spline enablement. The existing optional Break preceding splines setting still applies when explicitly selected.

Editor API sequence insertion follows the same rules. A named insertion requiring review returns awaiting_selection; poll its original requestId. Normal named insertion without overlaps completes immediately. Library pose insertion is unchanged.

Validation covers all three policies, inclusive bounds, fresh groups on repeated insertion, rounded-time merging, unchanged source data, ganged/individual overlap, the actual review window, Cancel, accepted insertion and one-step Undo. Both editors build with the same shared sources.
