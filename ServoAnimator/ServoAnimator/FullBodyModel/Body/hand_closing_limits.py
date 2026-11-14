"""Hand closing limits, radians. The single finger (thumb) is hard-mounted between
the two paired fingers (thumb_mount_details.py), so the digits curl in separate
lanes and cannot meet; each curl is limited only by its own range, sized to reach
the closed pose in the CAD drawing (finger_length_details.py sets the URDF).
Keep synchronized with ServoAnimator/HandClosingLimits.cs.
"""
BASE_MAXIMUM = 0.8
TIP_MAXIMUM = 1.0
END_MAXIMUM = 0.8
THUMB_MAXIMUM = 0.8
THUMB_TIP_MAXIMUM = 1.3
