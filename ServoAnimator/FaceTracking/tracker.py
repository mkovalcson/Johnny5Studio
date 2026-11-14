"""Local camera -> MediaPipe -> newline JSON. No robot access; prebuilt MediaPipe may send usage telemetry."""
import argparse, base64, json, math, sys, time
from pathlib import Path
import cv2
import numpy as np
import mediapipe as mp


def signals(result):
    values = {c.category_name: float(c.score) for c in result.face_blendshapes[0]}
    r = np.asarray(result.facial_transformation_matrixes[0])[:3, :3]
    # XYZ Euler angles; degrees relative to the camera, neutral removed in C#.
    values['headYaw'] = math.degrees(math.atan2(-r[2, 0], math.hypot(r[0, 0], r[1, 0])))
    values['headPitch'] = math.degrees(math.atan2(r[2, 1], r[2, 2]))
    values['headRoll'] = math.degrees(math.atan2(r[1, 0], r[0, 0]))
    values['browLeft'] = max(values.get('browOuterUpLeft', 0), values.get('browInnerUp', 0))
    values['browRight'] = max(values.get('browOuterUpRight', 0), values.get('browInnerUp', 0))
    values['squint'] = (values.get('eyeSquintLeft', 0) + values.get('eyeSquintRight', 0)) / 2
    # Expressive eye direction scores, not a calibrated screen gaze estimate.
    values['gazeX'] = (values.get('eyeLookOutLeft', 0) + values.get('eyeLookInRight', 0) - values.get('eyeLookInLeft', 0) - values.get('eyeLookOutRight', 0)) / 2
    values['gazeY'] = (values.get('eyeLookUpLeft', 0) + values.get('eyeLookUpRight', 0) - values.get('eyeLookDownLeft', 0) - values.get('eyeLookDownRight', 0)) / 2
    return values


def emit(packet):
    print(json.dumps(packet, allow_nan=False, separators=(',', ':')), flush=True)


class ProcessingRate:
    """Completed inference frames per elapsed second, refreshed about once a second."""
    def __init__(self, now):
        self.start = now
        self.frames = 0
        self.average = None
        self.camera_wait_total = self.face_processing_total = 0
        self.camera_wait_average = self.face_processing_average = None

    def completed(self, now, camera_wait_ms=0, face_processing_ms=0):
        self.frames += 1
        self.camera_wait_total += camera_wait_ms
        self.face_processing_total += face_processing_ms
        elapsed = now - self.start
        if elapsed >= 1:
            self.average = self.frames / elapsed
            self.camera_wait_average = self.camera_wait_total / self.frames
            self.face_processing_average = self.face_processing_total / self.frames
            self.start, self.frames = now, 0
            self.camera_wait_total = self.face_processing_total = 0
        return self.average


def open_capture(camera, fps):
    capture = cv2.VideoCapture(camera, cv2.CAP_DSHOW)
    capture.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
    capture.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
    capture.set(cv2.CAP_PROP_FPS, fps)
    # DirectShow's FPS setter can reopen capture without preserving FOURCC.
    # Apply MJPG last, then report the negotiated format after frames arrive.
    capture.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*'MJPG'))
    if not capture.isOpened():
        capture.release()
        raise RuntimeError('Cannot open camera. Check its index and camera permissions.')
    return capture


def capture_metadata(capture):
    reported = capture.get(cv2.CAP_PROP_FPS)
    fourcc = capture.get(cv2.CAP_PROP_FOURCC)
    pixel_format = ''.join(chr((int(fourcc) >> (8 * i)) & 255) for i in range(4)) if math.isfinite(fourcc) and fourcc > 0 else None
    pixel_format = pixel_format if pixel_format and all(c.isprintable() for c in pixel_format) else None
    return {'reportedCaptureFps': reported if math.isfinite(reported) and reported > 0 else None,
            'pixelFormat': pixel_format,
            'formatRequestAccepted': pixel_format == 'MJPG' if pixel_format else None,
            'backend': capture.getBackendName()}


def capture_only(camera, fps, warmup_seconds=2, measure_seconds=5):
    """Measure delivered/decoded frames without inference, preview or rate pacing."""
    capture = open_capture(camera, fps)
    try:
        emit({'phase': 'warming', 'requestedFps': fps})
        start = time.monotonic()
        while time.monotonic() - start < warmup_seconds:
            ok, _ = capture.read()
            if not ok:
                raise RuntimeError('Camera stopped delivering frames during warm-up.')
        emit({'phase': 'measuring', 'requestedFps': fps})
        start, frames, read_total = time.monotonic(), 0, 0
        while True:
            before = time.monotonic()
            ok, frame = capture.read()
            now = time.monotonic()
            if not ok:
                raise RuntimeError('Camera stopped delivering frames during measurement.')
            read_total += now - before
            frames += 1
            elapsed = now - start
            if elapsed >= measure_seconds:
                break
        emit({'phase': 'complete', 'requestedFps': fps, 'captureFps': frames / elapsed,
              'cameraWaitMs': read_total * 1000 / frames, 'frames': frames, 'seconds': elapsed,
              'captureWidth': int(frame.shape[1]), 'captureHeight': int(frame.shape[0]),
              **capture_metadata(capture)})
    finally:
        capture.release()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--camera', type=int, default=0)
    parser.add_argument('--fps', type=int, choices=(30, 60), default=30)
    parser.add_argument('--mirror', action='store_true', help='Mirror the image horizontally before face analysis and preview')
    parser.add_argument('--capture-test', action='store_true', help='Two-second warm-up plus five seconds capture-only measurement')
    parser.add_argument('--self-test', action='store_true')
    parser.add_argument('--image', help='Run one local test image without opening a camera')
    args = parser.parse_args()
    if args.capture_test:
        capture_only(args.camera, args.fps)
        return
    model = Path(__file__).with_name('face_landmarker.task')
    options = mp.tasks.vision.FaceLandmarkerOptions(
        base_options=mp.tasks.BaseOptions(model_asset_path=str(model)),
        running_mode=mp.tasks.vision.RunningMode.VIDEO, num_faces=1,
        output_face_blendshapes=True, output_facial_transformation_matrixes=True)
    with mp.tasks.vision.FaceLandmarker.create_from_options(options) as tracker:
        if args.image:
            frame = cv2.imread(args.image)
            if args.mirror:
                frame = cv2.flip(frame, 1)
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            result = tracker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), 0)
            values = signals(result) if result.face_landmarks else {}
            emit({'detected': bool(result.face_landmarks), 'values': values})
            if not values:
                raise RuntimeError('No face detected in test image')
            return
        if args.self_test:
            result = tracker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=np.zeros((480, 640, 3), dtype=np.uint8)), 0)
            emit({'selfTest': True, 'detected': bool(result.face_landmarks)})
            return
        capture = open_capture(args.camera, args.fps)
        start, last_preview, previous_ms = time.monotonic(), 0, -1
        processing_rate = ProcessingRate(start)
        metadata = None
        try:
            while True:
                tick = time.monotonic()
                ok, frame = capture.read()
                captured = time.monotonic()
                if not ok:
                    raise RuntimeError('Camera stopped delivering frames.')
                if metadata is None:
                    metadata = capture_metadata(capture)
                stamp = max(previous_ms + 1, int((captured - start) * 1000))
                previous_ms = stamp
                analysis_start = time.monotonic()
                if args.mirror:
                    frame = cv2.flip(frame, 1)
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                result = tracker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), stamp)
                detected = bool(result.face_landmarks)
                packet = {'timestampMs': stamp, 'detected': detected, 'values': signals(result) if detected else {}}
                face_processing_ms = (time.monotonic() - analysis_start) * 1000
                if tick - last_preview >= .2:
                    last_preview = tick
                    preview = cv2.resize(frame, (480, 360))
                    if detected:
                        for landmark in result.face_landmarks[0]:
                            cv2.circle(preview, (int(landmark.x * 480), int(landmark.y * 360)), 1, (70, 230, 90), -1)
                    ok, jpeg = cv2.imencode('.jpg', preview, [cv2.IMWRITE_JPEG_QUALITY, 65])
                    if ok:
                        packet['preview'] = base64.b64encode(jpeg).decode('ascii')
                packet['latencyMs'] = (time.monotonic() - tick) * 1000
                packet['processedFps'] = processing_rate.completed(time.monotonic(), (captured - tick) * 1000, face_processing_ms)
                packet.update(captureWidth=int(frame.shape[1]), captureHeight=int(frame.shape[0]),
                              cameraWaitMs=processing_rate.camera_wait_average,
                              faceProcessingMs=processing_rate.face_processing_average)
                packet.update(metadata)
                emit(packet)
                time.sleep(max(0, 1 / args.fps - (time.monotonic() - tick)))
        finally:
            capture.release()


if __name__ == '__main__':
    try:
        main()
    except (BrokenPipeError, KeyboardInterrupt):
        pass
    except Exception as error:
        emit({'error': str(error), 'detected': False})
        sys.exit(1)

