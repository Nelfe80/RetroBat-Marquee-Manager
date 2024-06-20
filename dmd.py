import ctypes
import os
import platform
import threading
import time
from PIL import Image, ImageSequence, UnidentifiedImageError, ImageOps
import serial
from serial.tools import list_ports
import win32pipe, win32file, pywintypes
import pefile
import cv2
from queue import Queue, Full, Empty

CACHE_DIR = 'dmd/cache'

def ensure_cache_dir():
    if not os.path.exists(CACHE_DIR):
        os.makedirs(CACHE_DIR)
        print(f"Cache directory created: {CACHE_DIR}")

def load_library():
    lib_dir = 'dmd'
    if platform.architecture()[0] == '64bit':
        lib_path = os.path.join(lib_dir, 'zedmd64.dll')
    else:
        lib_path = os.path.join(lib_dir, 'zedmd.dll')

    try:
        lib = ctypes.CDLL(lib_path)
        print(f"Loaded DLL: {lib_path}")

        functions = list_functions(lib_path)
        print("Functions exported by the DLL:")
        for func in functions:
            if not func.startswith('?'):
                print(func)

        return lib
    except OSError as e:
        print(f"Failed to load DLL: {e}")
        return None

def list_functions(dll_path):
    pe = pefile.PE(dll_path)
    functions = [entry.name.decode('utf-8') for entry in pe.DIRECTORY_ENTRY_EXPORT.symbols]
    return functions

def open_serial(port, baudrate=921600):
    try:
        ser = serial.Serial(port, baudrate, timeout=2)
        print(f"Connected to {port} at {baudrate} baud.")
        return ser
    except serial.SerialException as e:
        print(f"Failed to connect to {port}: {e}")
        return None

def getDMDSize(port, baudrate):
    ser = open_serial(port, baudrate)
    if not ser:
        return None, None
    try:
        ctrl_chars = bytes([0x5a, 0x65, 0x64, 0x72, 0x75, 0x6d])
        handshake_command = bytes([12])
        ser.write(ctrl_chars)
        ser.write(handshake_command)
        time.sleep(1.5)
        response = ser.read(20)
        print(f"Response: {response}")

        if response.startswith(b'Zedr') and len(response) >= 8:
            width = response[4] + (response[5] << 8)
            height = response[6] + (response[7] << 8)
            print(f"Handshake successful. Resolution: {width}x{height}")
            return width, height
        print(f"Handshake failed. Response: {response}")
        return None, None
    finally:
        ser.close()

def detect_com_ports_and_baudrates():
    baudrates = [9600, 921600, 460800, 230400, 115200, 57600, 38400, 19200]
    ports = list_ports.comports()
    for port in ports:
        for baudrate in baudrates:
            print(f"Testing {port.device} at {baudrate} baud...")
            width, height = getDMDSize(port.device, baudrate)
            if width and height:
                return port.device, baudrate, width, height
    return None, None, None, None

def resize_and_pad(image, target_width, target_height, limit_width=False):
    image_ratio = image.width / image.height
    target_ratio = target_width / target_height

    # Scale the image to the target height
    new_height = target_height
    new_width = int(new_height * image_ratio)
    if limit_width and new_width > target_width:
        new_width = target_width
        new_height = int(new_width / image_ratio)
    resized_image = image.resize((new_width, new_height))

    # Create a new image with transparent background
    new_image = Image.new("RGBA", (target_width, target_height), (0, 0, 0, 0))

    # Paste the resized image onto the new image
    offset = ((target_width - new_width) // 2, (target_height - new_height) // 2)
    new_image.paste(resized_image, offset)

    return new_image

def image_to_rgb_array(image, width, height):
    image = resize_and_pad(image, width, height)
    image = image.convert('RGB')
    data = image.tobytes()
    print(f"Image data length: {len(data)}")
    return (ctypes.c_uint8 * len(data))(*data)

def convert_image_to_gif(image_path, width, height):
    ensure_cache_dir()
    image = Image.open(image_path).convert("RGBA")
    image = resize_and_pad(image, width, height, limit_width=True)
    gif_path = os.path.join(CACHE_DIR, f"{os.path.splitext(os.path.basename(image_path))[0]}.gif")
    image.save(gif_path, format="GIF", save_all=True, duration=100, loop=0, transparency=0)
    print(f"Converted image to GIF and saved to cache: {gif_path}")
    return gif_path

def convert_mp4_to_gif(video_path, width, height, gif_temp_path, gif_final_path, single_frame_gif_path, lock):
    with lock:
        if os.path.exists(gif_final_path):
            print(f"Final GIF already exists: {gif_final_path}")
            return

        print(f"Converting MP4 to GIF in background: {video_path}")
        cap = cv2.VideoCapture(video_path)
        fps = cap.get(cv2.CAP_PROP_FPS)
        nskip = max(1, int(fps // 5))  # Skip frames to limit to 5 fps
        frames = []
        durations = []

        frame_number = 0
        while cap.isOpened():
            ret, cv2_im = cap.read()
            if not ret:
                break
            if frame_number % nskip == 0:
                im = Image.fromarray(cv2.cvtColor(cv2_im, cv2.COLOR_BGR2RGB)).convert("RGBA")
                im = resize_and_pad(im, width, height)
                frames.append(im)
                durations.append(int((nskip / fps) * 1000))
            frame_number += 1

        cap.release()
        frames[0].save(gif_temp_path, format='GIF', append_images=frames[1:], save_all=True, duration=durations, loop=0, transparency=0)
        print(f"Converted MP4 to GIF and saved to cache: {gif_temp_path}")

        # Ensure the file is properly closed before renaming
        while True:
            try:
                os.rename(gif_temp_path, gif_final_path)
                print(f"Renamed temporary GIF to final GIF: {gif_final_path}")
                break
            except PermissionError:
                print(f"PermissionError: Retrying renaming {gif_temp_path} to {gif_final_path}")
                time.sleep(1)

        # Clean up single frame and temporary GIF
        if os.path.exists(gif_temp_path):
            os.remove(gif_temp_path)
            print(f"Removed temporary GIF: {gif_temp_path}")
        if os.path.exists(single_frame_gif_path):
            os.remove(single_frame_gif_path)
            print(f"Removed single-frame GIF: {single_frame_gif_path}")

def create_single_frame_gif(video_path, width, height, single_frame_gif_path):
    print(f"Creating single-frame GIF from MP4: {video_path}")

    cap = cv2.VideoCapture(video_path)
    ret, cv2_im = cap.read()
    if ret:
        im = Image.fromarray(cv2.cvtColor(cv2_im, cv2.COLOR_BGR2RGB)).convert("RGBA")
        im = resize_and_pad(im, width, height)
        im.save(single_frame_gif_path, format='GIF', transparency=0)
        print(f"Created single-frame GIF and saved to cache: {single_frame_gif_path}")
    cap.release()

class ZeDMD(ctypes.Structure):
    pass

lib = load_library()
if lib:
    ZeDMD_ptr = ctypes.POINTER(ZeDMD)
    lib.ZeDMD_GetInstance.restype = ZeDMD_ptr
    lib.ZeDMD_GetInstance.argtypes = []

    lib.ZeDMD_Open.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_Open.restype = ctypes.c_bool

    lib.ZeDMD_Close.argtypes = [ZeDMD_ptr]

    lib.ZeDMD_RenderRgb24.argtypes = [ZeDMD_ptr, ctypes.POINTER(ctypes.c_uint8)]

    lib.ZeDMD_ClearScreen.argtypes = [ZeDMD_ptr]

    lib.ZeDMD_SetFrameSize.argtypes = [ZeDMD_ptr, ctypes.c_uint16, ctypes.c_uint16]

    lib.ZeDMD_LedTest.argtypes = [ZeDMD_ptr]

    lib.ZeDMD_EnableUpscaling.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_DisableUpscaling.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_EnablePreDownscaling.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_DisablePreDownscaling.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_EnablePreUpscaling.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_DisablePreUpscaling.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_EnforceStreaming.argtypes = [ZeDMD_ptr]

    lib.ZeDMD_EnableDebug.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_DisableDebug.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_EnableDebug.restype = ctypes.c_bool
    lib.ZeDMD_DisableDebug.restype = ctypes.c_bool

    class ZeDMD:
        def __init__(self):
            self.obj = lib.ZeDMD_GetInstance()
            if not self.obj:
                print("Failed to get ZeDMD instance")

        def open(self):
            return lib.ZeDMD_Open(self.obj)

        def render_rgb24(self, frame):
            lib.ZeDMD_RenderRgb24(self.obj, frame)

        def clear_screen(self):
            lib.ZeDMD_ClearScreen(self.obj)

        def set_frame_size(self, width, height):
            lib.ZeDMD_SetFrameSize(self.obj, width, height)

        def led_test(self):
            lib.ZeDMD_LedTest(self.obj)

        def close(self):
            lib.ZeDMD_Close(self.obj)

        def enable_upscaling(self):
            lib.ZeDMD_EnableUpscaling(self.obj)

        def disable_upscaling(self):
            lib.ZeDMD_DisableUpscaling(self.obj)

        def enable_pre_downscaling(self):
            lib.ZeDMD_EnablePreDownscaling(self.obj)

        def disable_pre_downscaling(self):
            lib.ZeDMD_DisablePreDownscaling(self.obj)

        def enable_pre_upscaling(self):
            lib.ZeDMD_EnablePreUpscaling(self.obj)

        def disable_pre_upscaling(self):
            lib.ZeDMD_DisablePreUpscaling(self.obj)

        def enforce_streaming(self):
            lib.ZeDMD_EnforceStreaming(self.obj)

        def enable_debug(self):
            if lib.ZeDMD_EnableDebug(self.obj):
                print("Debug mode enabled.")
            else:
                print("Failed to enable debug mode.")

        def disable_debug(self):
            if lib.ZeDMD_DisableDebug(self.obj):
                print("Debug mode disabled.")
            else:
                print("Failed to disable debug mode.")

class DMDServer:
    def __init__(self, pipe_name):
        self.pipe_name = pipe_name
        self.zedmd = ZeDMD()
        self.zedmd_open = False
        self.last_image = None
        self.last_width = None
        self.last_height = None
        self.gif_frames = []
        self.gif_durations = []
        self.gif_lock = threading.Lock()
        self.stop_event = threading.Event()
        self.image_buffer = []
        self.conversion_locks = {}
        self.last_request_time = time.time()
        self.last_client_activity = time.time()  # To track client activity
        self.last_request = None
        self.display_count = 0
        self.image_queue = Queue(maxsize=2)
        self.pipe = None
        self.keep_alive_thread = None
        self.process_image_thread = None

    def start(self):
        print("Starting server...")
        port, baudrate, width, height = self.detect_dmd_size()
        if port and baudrate and width and height:
            print("Opening ZeDMD...")
            if self.zedmd.open():
                self.zedmd_open = True
                self.zedmd.clear_screen()
                self.zedmd.enable_upscaling()
                self.zedmd.enable_pre_downscaling()
                self.zedmd.enable_pre_upscaling()
                self.zedmd.enforce_streaming()
                self.last_request_time = time.time()
                self.last_client_activity = time.time()  # Update client activity time
                self.display_count = 0
                print("ZeDMD opened successfully.")
                print("Default image display")
                if self.last_image and self.last_width and self.last_height:
                    self.display_image(self.last_image, self.last_width, self.last_height)
                else:
                    self.display_image('images/default.png', width, height)
            else:
                print("Failed to open ZeDMD")
                return

    def listen_for_clients(self):
        self.keep_alive_thread = threading.Thread(target=self.keep_dmd_alive, daemon=True)
        self.keep_alive_thread.start()
        self.process_image_thread = threading.Thread(target=self.process_image_queue, daemon=True)
        self.process_image_thread.start()
        while True:
            print(f"Waiting for client connection on {self.pipe_name}...")
            try:
                self.pipe = win32pipe.CreateNamedPipe(
                    self.pipe_name,
                    win32pipe.PIPE_ACCESS_DUPLEX,
                    win32pipe.PIPE_TYPE_MESSAGE | win32pipe.PIPE_READMODE_MESSAGE | win32pipe.PIPE_WAIT,
                    1, 65536, 65536, 0, None)
                win32pipe.ConnectNamedPipe(self.pipe, None)
                print(f"Client connected on {self.pipe_name}")
                self.stop_animation()
                self.handle_client(self.pipe, self.last_width, self.last_height)
            except pywintypes.error as e:
                print(f"CreateNamedPipe error: {e}")
                if e.winerror == 231:  # All pipe instances are busy
                    time.sleep(1)
                else:
                    break

    def handle_client(self, pipe, width, height):
        try:
            while True:
                resp = win32file.ReadFile(pipe, 64 * 1024)
                if not resp:
                    break
                command = resp[1].decode().strip()
                print(f"Received command: {command} at {time.time()}")
                if command.startswith('loadfile'):
                    start_idx = command.find('"') + 1
                    end_idx = command.rfind('"')
                    image_path = command[start_idx:end_idx]
                    print(f"Full image path: {image_path}")
                    self.last_request_time = time.time()
                    self.last_client_activity = time.time()  # Update client activity time
                    self.last_request = (image_path, width, height)
                    try:
                        self.image_queue.put_nowait((image_path, width, height))
                    except Full:
                        pass  # Ignore if the queue is full
                    print(f"Queue state after adding: {[item for item in self.image_queue.queue]} at {time.time()}")
        except pywintypes.error as e:
            if e.winerror != 109:  # Ignore "pipe closed" error
                print(f"Error handling client: {e}")
        finally:
            win32file.CloseHandle(pipe)

    def process_image_queue(self):
        while True:
            try:
                image_path, width, height = self.image_queue.get(timeout=1)
                # Only process if this is the most recent request
                if self.image_queue.empty():
                    self.display_image(image_path, width, height)
                print(f"Queue state after removing: {[item for item in self.image_queue.queue]} at {time.time()}")
            except Empty:
                pass  # Ignore if the queue is empty

    def display_image(self, image_path, width, height):
        self.display_count += 1
        if self.display_count >= 10:
            print("Clearing screen...")
            #self.zedmd.clear_screen()
            self.display_count = 0

        image_path = os.path.normpath(image_path)
        if self.zedmd.obj is None:
            print("ZeDMD instance not available.")
            return
        if not self.zedmd_open:
            print("ZeDMD is not open.")
            return
        self.last_image = image_path
        self.last_width = width
        self.last_height = height

        start_time = time.time()  # Start timing

        self.zedmd.set_frame_size(width, height)

        gif_single_frame_path = os.path.join(CACHE_DIR, f"{os.path.splitext(os.path.basename(image_path))[0]}_single_frame.gif")
        gif_final_path = os.path.join(CACHE_DIR, f"{os.path.splitext(os.path.basename(image_path))[0]}.gif")
        gif_temp_path = os.path.join(CACHE_DIR, f"{os.path.splitext(os.path.basename(image_path))[0]}_temp.gif")
        conversion_lock = self.conversion_locks.setdefault(image_path, threading.Lock())

        if image_path.lower().endswith('.gif'):
            gif_path = image_path
        else:
            if os.path.exists(gif_final_path):
                print(f"Loading GIF from cache: {gif_final_path}")
                gif_path = gif_final_path
            elif os.path.exists(gif_single_frame_path):
                print(f"Single-frame GIF already exists: {gif_single_frame_path}")
                gif_path = gif_single_frame_gif_path
                if not os.path.exists(gif_final_path) and not os.path.exists(gif_temp_path):
                    print(f"Launching background conversion of MP4 to GIF: {image_path}")
                    threading.Thread(target=convert_mp4_to_gif, args=(image_path, width, height, gif_temp_path, gif_final_path, gif_single_frame_path, conversion_lock), daemon=True).start()
            else:
                if image_path.lower().endswith('.mp4'):
                    print(f"Creating single-frame GIF from MP4: {image_path}")
                    create_single_frame_gif(image_path, width, height, gif_single_frame_path)
                    gif_path = gif_single_frame_path
                    if not os.path.exists(gif_final_path) and not os.path.exists(gif_temp_path):
                        print(f"Launching background conversion of MP4 to GIF: {image_path}")
                        threading.Thread(target=convert_mp4_to_gif, args=(image_path, width, height, gif_temp_path, gif_final_path, gif_single_frame_path, conversion_lock), daemon=True).start()
                else:
                    print(f"Converting image to GIF: {image_path}")
                    gif_path = convert_image_to_gif(image_path, width, height)

        self.wait_for_file(gif_path)

        middle_time = time.time()  # Intermediate timing

        if os.path.exists(gif_path):
            self.display_gif(gif_path, width, height)

        end_time = time.time()  # End timing

        print(f"Time to set frame size and process image: {middle_time - start_time:.6f} seconds")
        print(f"Time to wait for file and display GIF: {end_time - middle_time:.6f} seconds")

    def wait_for_file(self, file_path):
        while not os.path.exists(file_path):
            print(f"Waiting for file: {file_path}")
            time.sleep(0.1)
        # Additional wait to ensure file is fully written
        time.sleep(0.5)

    def display_gif(self, image_path, width, height):
        with self.gif_lock:
            print(f"Displaying GIF: {image_path}")
            try:
                image = Image.open(image_path)
                self.gif_frames = [frame.copy() for frame in ImageSequence.Iterator(image)]
                self.gif_durations = [frame.info.get('duration', 100) / 1000.0 for frame in ImageSequence.Iterator(image)]
                self.stop_event.clear()
            except UnidentifiedImageError:
                print(f"Unidentified image error for: {image_path}")
                return
        if len(self.gif_frames) == 0:
            print("No frames found in GIF, cannot display.")
            return
        if len(self.gif_frames) == 1:
            print("GIF has only one frame, rendering once.")
            rgb_frame = image_to_rgb_array(self.gif_frames[0], width, height)
            self.zedmd.render_rgb24(rgb_frame)
        else:
            threading.Thread(target=self.play_gif, args=(width, height), daemon=True).start()

    def play_gif(self, width, height):
        while self.zedmd_open and self.gif_frames and not self.stop_event.is_set():
            with self.gif_lock:
                for frame, duration in zip(self.gif_frames, self.gif_durations):
                    if self.stop_event.is_set():
                        break
                    start_time = time.time()
                    rgb_frame = image_to_rgb_array(frame, width, height)
                    self.zedmd.render_rgb24(rgb_frame)
                    end_time = time.time()
                    print(f"GIF Time to convert and render frame: {end_time - start_time:.6f} seconds")
                    time.sleep(duration)
            # Ensure GIF loops indefinitely
            if not self.stop_event.is_set():
                print("Restarting GIF loop.")
                time.sleep(0.1)

    def stop_animation(self):
        self.stop_event.set()
        with self.gif_lock:
            self.gif_frames = []
            self.gif_durations = []

    def keep_dmd_alive(self):
        while True:
            current_time = time.time()
            elapsed_time = current_time - self.last_client_activity
            print(f"Elapsed time since last client activity: {elapsed_time:.2f} seconds")

            # Check for client inactivity
            if elapsed_time >= 600:  # 10 minutes = 600
                print("Restarting DMDServer due to inactivity...")
                print("Closing server for restart...")
                self.close()
                print("Server closed. Restarting...")
                self.start()
                print("Server restarted and last image displayed.")
            if self.last_image and self.last_width and self.last_height:
                self.display_image(self.last_image, self.last_width, self.last_height)
            time.sleep(5)

    def detect_dmd_size(self):
        port, baudrate, width, height = detect_com_ports_and_baudrates()
        if port and baudrate:
            return port, baudrate, width, height
        return None, None, None, None

    def close(self):
        print(f"Close server starting...")
        if self.zedmd_open:
            print(f"zedmd close")
            self.zedmd.close()
            self.zedmd_open = False

if __name__ == "__main__":
    ensure_cache_dir()
    server = DMDServer(r'\\.\pipe\dmd-pipe')
    try:
        server.zedmd.enable_debug()  # Activer le mode débogage avant de démarrer le serveur
        server.start()
        server.listen_for_clients()
    except KeyboardInterrupt:
        print("Shutting down server...")
        server.close()
        server.zedmd.disable_debug()  # Désactiver le mode débogage avant de quitter
