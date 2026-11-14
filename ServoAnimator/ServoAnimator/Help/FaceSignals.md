# Face Signal Reference

Signal names are case-sensitive identifiers in the mapping dropdown. The list contains 52 MediaPipe blendshape scores (including _neutral) and ten derived signals. Expression scores generally span 0–1 and are strengths, not physical distances or angles.

## Derived signals

- headYaw, headPitch, headRoll: camera-relative orientation in degrees. Set neutral before mapping; use Reverse to match your desired direction.
- gazeX, gazeY: approximate combined eye-look direction, generally −1..+1. These are not calibrated screen gaze.
- browLeft, browRight: the larger of that side's outer brow-up score and the shared inner brow-up score.
- squint: average of eyeSquintLeft and eyeSquintRight.
- LPiston, RPiston: normalized −1..+1 pitch/roll combinations, controlled by the Piston signals settings group.

## MediaPipe expression identifiers

### Neutral

- _neutral

### Brows

- browDownLeft
- browDownRight
- browInnerUp
- browOuterUpLeft
- browOuterUpRight

### Cheeks

- cheekPuff
- cheekSquintLeft
- cheekSquintRight

### Eyes

- eyeBlinkLeft
- eyeBlinkRight
- eyeLookDownLeft
- eyeLookDownRight
- eyeLookInLeft
- eyeLookInRight
- eyeLookOutLeft
- eyeLookOutRight
- eyeLookUpLeft
- eyeLookUpRight
- eyeSquintLeft
- eyeSquintRight
- eyeWideLeft
- eyeWideRight

### Jaw

- jawForward
- jawLeft
- jawOpen
- jawRight

### Mouth

- mouthClose
- mouthDimpleLeft
- mouthDimpleRight
- mouthFrownLeft
- mouthFrownRight
- mouthFunnel
- mouthLeft
- mouthLowerDownLeft
- mouthLowerDownRight
- mouthPressLeft
- mouthPressRight
- mouthPucker
- mouthRight
- mouthRollLower
- mouthRollUpper
- mouthShrugLower
- mouthShrugUpper
- mouthSmileLeft
- mouthSmileRight
- mouthStretchLeft
- mouthStretchRight
- mouthUpperUpLeft
- mouthUpperUpRight

### Nose

- noseSneerLeft
- noseSneerRight

Select the desired signal, then set appropriate InputMin/InputMax and native OutputMin/OutputMax. Exposing a signal does not enable or map it automatically. Separate left/right signals allow asymmetric animation. Set neutral subtracts a baseline, which can reduce the useful range of expression signals; adjust the row Neutral if that is not wanted.
