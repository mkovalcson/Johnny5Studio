import sys
import unittest
from types import SimpleNamespace
from unittest.mock import MagicMock, patch

import numpy as np
import tracker


class FrameRateChecks(unittest.TestCase):
    def test_mirroring_reaches_both_analysis_and_preview(self):
        # Asymmetric colors expose both a missing flip and a preview-only flip.
        frame = np.zeros((4, 6, 3), dtype=np.uint8)
        frame[:, :2] = [10, 40, 220]
        frame[:, 2:] = [190, 80, 20]
        for mirror in (False, True):
            capture = MagicMock()
            capture.isOpened.return_value = True
            capture.get.return_value = 30
            capture.getBackendName.return_value = 'DSHOW'
            capture.read.side_effect = [(True, frame.copy()), (False, None)]
            model = MagicMock()
            analysis = model.__enter__.return_value.detect_for_video
            analysis.return_value = SimpleNamespace(face_landmarks=[])
            argv = ['tracker.py'] + (['--mirror'] if mirror else [])
            expected = frame[:, ::-1] if mirror else frame
            with patch.object(sys, 'argv', argv), \
                 patch.object(tracker.mp.tasks.vision.FaceLandmarker, 'create_from_options', return_value=model), \
                 patch.object(tracker.cv2, 'VideoCapture', return_value=capture), \
                 patch.object(tracker.time, 'monotonic', return_value=100), \
                 patch.object(tracker.time, 'sleep'), patch.object(tracker, 'emit'), \
                 patch.object(tracker.cv2, 'imencode', return_value=(False, None)) as encode:
                with self.assertRaisesRegex(RuntimeError, 'stopped delivering'):
                    tracker.main()
                np.testing.assert_array_equal(analysis.call_args.args[0].numpy_view(), expected[:, :, ::-1])
                np.testing.assert_array_equal(encode.call_args.args[1], tracker.cv2.resize(expected, (480, 360)))

    def test_measured_rate_instead_of_requested_rate(self):
        for rate in (15, 30, 48, 60):
            average = tracker.ProcessingRate(0)
            self.assertIsNone(average.completed(.001))
            average = tracker.ProcessingRate(0)
            for frame in range(1, rate + 1):
                value = average.completed(frame / rate)
            self.assertAlmostEqual(value, rate)
            self.assertAlmostEqual(average.completed(1.01), rate)

    def test_slow_frames_count_wall_time(self):
        average = tracker.ProcessingRate(0)
        self.assertAlmostEqual(average.completed(2), .5)

    def test_stage_averages_share_the_fps_window(self):
        average = tracker.ProcessingRate(0)
        average.completed(.4, 20, 5)
        self.assertIsNone(average.camera_wait_average)
        average.completed(1.2, 40, 15)
        self.assertAlmostEqual(average.camera_wait_average, 30)
        self.assertAlmostEqual(average.face_processing_average, 10)
        average.completed(2.3, 8, 3)
        self.assertAlmostEqual(average.camera_wait_average, 8)
        self.assertAlmostEqual(average.face_processing_average, 3)

    def test_capture_and_processing_use_selected_rate(self):
        for rate in (30, 60):
            capture = MagicMock()
            capture.isOpened.return_value = True
            capture.get.return_value = 30
            capture.getBackendName.return_value = 'DSHOW'
            capture.read.side_effect = [(True, np.zeros((480, 640, 3), dtype=np.uint8)), (False, None)]
            model = MagicMock()
            model.__enter__.return_value.detect_for_video.return_value = SimpleNamespace(face_landmarks=[])
            with patch.object(sys, 'argv', ['tracker.py', '--fps', str(rate)]), \
                 patch.object(tracker.mp.tasks.vision.FaceLandmarker, 'create_from_options', return_value=model), \
                 patch.object(tracker.cv2, 'VideoCapture', return_value=capture), \
                 patch.object(tracker.time, 'monotonic', return_value=100), \
                 patch.object(tracker.time, 'sleep') as sleep, patch.object(tracker, 'emit') as emit:
                with self.assertRaisesRegex(RuntimeError, 'stopped delivering'):
                    tracker.main()
                capture.set.assert_any_call(tracker.cv2.CAP_PROP_FPS, rate)
                capture.set.assert_any_call(tracker.cv2.CAP_PROP_FOURCC, tracker.cv2.VideoWriter_fourcc(*'MJPG'))
                sleep.assert_called_once_with(1 / rate)
                self.assertIn('processedFps', emit.call_args.args[0])
                packet = emit.call_args.args[0]
                self.assertEqual((packet['captureWidth'], packet['captureHeight']), (640, 480))
                self.assertEqual(packet['reportedCaptureFps'], 30)
                self.assertIsNone(packet['cameraWaitMs'])
                self.assertIsNone(packet['faceProcessingMs'])
                capture.release.assert_called_once()

    def test_pipeline_measures_camera_and_face_separately(self):
        capture = MagicMock()
        capture.isOpened.return_value = True
        capture.get.return_value = float('nan')
        capture.getBackendName.return_value = 'DSHOW'
        capture.read.side_effect = [(True, np.zeros((480, 640, 3), dtype=np.uint8)), (False, None)]
        model = MagicMock()
        model.__enter__.return_value.detect_for_video.return_value = SimpleNamespace(face_landmarks=[])
        with patch.object(sys, 'argv', ['tracker.py', '--fps', '60']), \
             patch.object(tracker.mp.tasks.vision.FaceLandmarker, 'create_from_options', return_value=model), \
             patch.object(tracker.cv2, 'VideoCapture', return_value=capture), \
             patch.object(tracker.time, 'monotonic', side_effect=[100, 100, 100.020, 100.020, 100.025, 101, 101, 101, 101, 101]), \
             patch.object(tracker.time, 'sleep'), patch.object(tracker, 'emit') as emit:
            with self.assertRaisesRegex(RuntimeError, 'stopped delivering'):
                tracker.main()
            packet = emit.call_args.args[0]
            self.assertAlmostEqual(packet['cameraWaitMs'], 20)
            self.assertAlmostEqual(packet['faceProcessingMs'], 5)
            self.assertAlmostEqual(packet['processedFps'], 1)
            self.assertIsNone(packet['reportedCaptureFps'])

    def test_capture_only_measures_delivery_without_analysis_or_pacing(self):
        for rate in (30, 60):
            clock = [0.0]
            frame = np.zeros((480, 640, 3), dtype=np.uint8)
            def read():
                clock[0] += 1 / rate
                return True, frame
            capture = MagicMock()
            capture.isOpened.return_value = True
            capture.read.side_effect = read
            capture.get.side_effect = lambda prop: 60 if prop == tracker.cv2.CAP_PROP_FPS else tracker.cv2.VideoWriter_fourcc(*'MJPG')
            capture.getBackendName.return_value = 'DSHOW'
            with patch.object(sys, 'argv', ['tracker.py', '--fps', '60', '--capture-test']), \
                 patch.object(tracker.cv2, 'VideoCapture', return_value=capture), \
                 patch.object(tracker.mp.tasks.vision.FaceLandmarker, 'create_from_options') as analysis, \
                 patch.object(tracker.time, 'monotonic', side_effect=lambda: clock[0]), \
                 patch.object(tracker.time, 'sleep') as sleep, patch.object(tracker, 'emit') as emit:
                tracker.main()
                report = emit.call_args.args[0]
                self.assertEqual(report['phase'], 'complete')
                capture.set.assert_any_call(tracker.cv2.CAP_PROP_FOURCC, tracker.cv2.VideoWriter_fourcc(*'MJPG'))
                self.assertAlmostEqual(report['captureFps'], rate)
                self.assertAlmostEqual(report['cameraWaitMs'], 1000 / rate)
                self.assertEqual(report['reportedCaptureFps'], 60)
                self.assertEqual(report['pixelFormat'], 'MJPG')
                self.assertGreaterEqual(report['seconds'], 5)
                analysis.assert_not_called()
                sleep.assert_not_called()
                capture.release.assert_called_once()

    def test_capture_failure_releases_the_camera(self):
        capture = MagicMock()
        capture.isOpened.return_value = True
        capture.read.return_value = False, None
        with patch.object(tracker.cv2, 'VideoCapture', return_value=capture), patch.object(tracker, 'emit'):
            with self.assertRaisesRegex(RuntimeError, 'warm-up'):
                tracker.capture_only(0, 60)
        capture.release.assert_called_once()

    def test_directshow_fps_reset_does_not_undo_the_format_request(self):
        mjpg = tracker.cv2.VideoWriter_fourcc(*'MJPG')
        yuy2 = tracker.cv2.VideoWriter_fourcc(*'YUY2')
        for accept_format in (True, False):
            state = {'format': yuy2, 'fps': 30}
            capture = MagicMock()
            capture.isOpened.return_value = True
            capture.getBackendName.return_value = 'DSHOW'
            def set_property(prop, value):
                if prop == tracker.cv2.CAP_PROP_FPS:
                    state.update(format=yuy2, fps=value)
                elif prop == tracker.cv2.CAP_PROP_FOURCC and accept_format:
                    state['format'] = value
                return accept_format
            capture.set.side_effect = set_property
            capture.get.side_effect = lambda prop: state['fps'] if prop == tracker.cv2.CAP_PROP_FPS else state['format']
            with patch.object(tracker.cv2, 'VideoCapture', return_value=capture):
                opened = tracker.open_capture(0, 60)
            metadata = tracker.capture_metadata(opened)
            self.assertEqual(state['fps'], 60)
            self.assertEqual(state['format'], mjpg if accept_format else yuy2)
            self.assertEqual(metadata['formatRequestAccepted'], accept_format)
            self.assertEqual(metadata['pixelFormat'], 'MJPG' if accept_format else 'YUY2')


if __name__ == '__main__':
    unittest.main()
