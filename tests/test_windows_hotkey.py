import sys
import threading
import time
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

from windows_hotkey import WindowsHotkeyBackend, _parse_binding, _virtual_key


class WindowsHotkeyMappingTests(unittest.TestCase):
    def test_parses_supported_binding(self):
        self.assertEqual(_parse_binding("Ctrl + Shift + F1"), (("ctrl", "shift"), "f1"))

    def test_maps_all_function_key_boundaries(self):
        self.assertEqual(_virtual_key("f1"), 0x70)
        self.assertEqual(_virtual_key("f24"), 0x87)
        with self.assertRaises(ValueError):
            _virtual_key("f12")

    def test_maps_letters_digits_and_named_keys(self):
        self.assertEqual(_virtual_key("a"), ord("A"))
        self.assertEqual(_virtual_key("7"), ord("7"))
        self.assertEqual(_virtual_key("space"), 0x20)

    def test_rejects_invalid_bindings(self):
        with self.assertRaises(ValueError):
            _parse_binding("")
        with self.assertRaises(ValueError):
            _parse_binding("hyper+f1")
        with self.assertRaises(ValueError):
            _virtual_key("volume_up")

    def test_rejects_windows_reserved_bindings(self):
        with self.assertRaises(ValueError):
            _parse_binding("cmd+l")


@unittest.skipUnless(sys.platform == "win32", "Win32 integration test")
class WindowsHotkeyIntegrationTests(unittest.TestCase):
    def test_native_press_release_survives_idle_and_repetition(self):
        import ctypes

        pressed = threading.Event()
        released = threading.Event()
        counts = [0, 0]

        def on_press():
            counts[0] += 1
            pressed.set()

        def on_release():
            counts[1] += 1
            released.set()

        stop_event = threading.Event()
        backend = WindowsHotkeyBackend("f24", on_press, on_release, stop_event)
        backend.start()
        user32 = ctypes.WinDLL("user32", use_last_error=True)
        vk_f24 = 0x87
        key_up = 0x0002
        try:
            time.sleep(1.0)
            for expected in range(1, 21):
                pressed.clear()
                released.clear()
                user32.keybd_event(vk_f24, 0, 0, 0)
                self.assertTrue(pressed.wait(1.0), f"press {expected} was lost")
                user32.keybd_event(vk_f24, 0, key_up, 0)
                self.assertTrue(released.wait(1.0), f"release {expected} was lost")
            self.assertEqual(counts, [20, 20])
        finally:
            user32.keybd_event(vk_f24, 0, key_up, 0)
            stop_event.set()
            backend.stop()


if __name__ == "__main__":
    unittest.main()
