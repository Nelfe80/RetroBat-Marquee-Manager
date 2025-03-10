import ctypes
import os
import platform
import threading
import time
from PIL import Image, ImageSequence, UnidentifiedImageError, ImageOps, ImageDraw, ImageFont
import serial
from serial.tools import list_ports
import win32pipe, win32file, pywintypes
import pefile
import cv2
from queue import Queue, Full, Empty
import numpy as np
import configparser
import logging
from watchdog.observers import Observer
from watchdog.events import FileSystemEventHandler

CACHE_DIR = 'dmd/cache'

# Définir le type de callback (ici, le callback attend un c_char_p et ne renvoie rien)
LOG_CALLBACK_TYPE = ctypes.CFUNCTYPE(None, ctypes.c_char_p)

def my_log_callback(message):
    # Convertir le message depuis C en Python (si nécessaire)
    log_message = message.decode('utf-8')
    print("DMD Log:", log_message)

# Créer une instance du callback
log_callback_c = LOG_CALLBACK_TYPE(my_log_callback)

def ensure_cache_dir():
    if not os.path.exists(CACHE_DIR):
        os.makedirs(CACHE_DIR)
        print(f"Cache directory created: {CACHE_DIR}")

def load_config():
    global config
    config = configparser.ConfigParser()
    config.read('config.ini')
    if config['Settings'].get("logFile", "false").lower() == "true":
        logging.basicConfig(filename="ESEvents.log", level=logging.INFO)
        logging.getLogger('werkzeug').setLevel(logging.INFO)
        logging.info("Start logging")
    else:
        logging.basicConfig(level=logging.INFO)
    current_working_dir = os.getcwd()
    logging.info(f"Working directory: {current_working_dir}")
    if 'ActiveDMD' not in config['Settings']:
        config['Settings']['ActiveDMD'] = "false"

class FileWatcher(FileSystemEventHandler):
    def __init__(self, file_path, callback):
        self.file_path = os.path.normcase(file_path)
        self.callback = callback

    def on_modified(self, event):
        if os.path.normcase(event.src_path) == self.file_path:
            logging.info(f"Modification détectée sur {self.file_path}")
            self.callback()

def on_file_modified():
    logging.info("Le fichier marquee a changé, actualisation du DMD.")
    try:
        server.stop_animation()
        width = server.last_width if server.last_width is not None else 128
        height = server.last_height if server.last_height is not None else 64
        marquee_image_path = os.path.join(CACHE_DIR, '_cache_dmd.png')
        # Supprimer le cache sur disque si existant (pour forcer la mise à jour)
        gif_path = os.path.join(CACHE_DIR, '_cache_dmd.gif')
        if os.path.exists(gif_path):
            os.remove(gif_path)
            logging.info(f"Supprimé {gif_path}")
        server.display_image(marquee_image_path, width, height)
    except Exception as e:
        logging.error(f"Erreur lors de l'actualisation du DMD : {e}")

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

def getDMDSize(port, baudrate=921600):
    ser = open_serial(port, baudrate)
    if not ser:
        return None, None
    try:
        ser.reset_input_buffer()
        time.sleep(0.2)
        FRAME_SIZE = 64
        FRAME_HEADER = b"FRAME"
        CTRL_CHARS_HEADER = b"ZeDMD"
        #CTRL_CHARS_HEADER = b"\x80\x80\x00\x80"
        HANDSHAKE_COMMAND = 0x0c
        packet = bytearray(FRAME_SIZE)
        packet[0:5] = FRAME_HEADER
        packet[5:10] = CTRL_CHARS_HEADER
        packet[10] = HANDSHAKE_COMMAND
        ser.write(packet)
        ser.flush()
        time.sleep(0.5)  # Temps d'attente pour le handshake
        response = ser.read(FRAME_SIZE)
        print("Response received:", response.hex())
        if len(response) < FRAME_SIZE:
            print(f"Handshake failed: incomplete response ({len(response)} bytes received).")
            return None, None
        if response[0:4] != CTRL_CHARS_HEADER[0:4]:
            print(f"Handshake failed: header mismatch. Received header: {response[0:4]}")
            return None, None
        if response[57] != ord('R') or response[8] == 0:
            print("Handshake failed: response validation did not pass.")
            return None, None
        width = response[4] + (response[5] << 8)
        height = response[6] + (response[7] << 8)
        print(f"Handshake successful. Resolution: {width}x{height}")
        return width, height
    finally:
        ser.close()

def detect_com_ports_and_baudrates():
    baudrates = [921600, 460800, 230400, 115200, 57600, 38400, 19200, 9600]
    ports = list_ports.comports()
    for port in ports:
        for baudrate in baudrates:
            print(f"Testing {port.device} at {baudrate} baud...")
            width, height = getDMDSize(port.device, baudrate)
            if width and height:
                return port.device, baudrate, width, height
    return None, None, None, None

def resize_and_pad(image, target_width, target_height, limit_width=False, limit_height=False):
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

def resize_and_crop(image, target_width, target_height):
    """
    Redimensionne l'image en conservant son ratio (homothétie) de façon à ce que
    l'image couvre entièrement les dimensions cibles. Si l'image n'est pas exactement
    aux dimensions cibles, elle sera recadrée au centre pour supprimer les excès
    sur les côtés (gauche et droite) ou en haut et en bas.
    """
    # Ratio cible et ratio de l'image
    target_ratio = target_width / target_height
    img_ratio = image.width / image.height

    if img_ratio > target_ratio:
        # L'image est plus large que la cible.
        # On redimensionne en fonction de la hauteur pour que la hauteur soit exactement target_height.
        new_height = target_height
        new_width = int(new_height * img_ratio)
    else:
        # L'image est moins large (ou égale) que la cible.
        # On redimensionne en fonction de la largeur pour que la largeur soit exactement target_width.
        new_width = target_width
        new_height = int(new_width / img_ratio)

    # Redimensionnement avec LANCZOS (qualité et rapidité)
    image_resized = image.resize((new_width, new_height), Image.LANCZOS)

    # Calcul des coordonnées pour recadrer l'image centrée sur le DMD
    left = (new_width - target_width) // 2
    top = (new_height - target_height) // 2
    right = left + target_width
    bottom = top + target_height

    image_cropped = image_resized.crop((left, top, right, bottom))
    return image_cropped

def image_to_rgb565_array(image, target_width, target_height):
    """
    Convertit l'image en RGB565 de façon rapide.
    Si l'image n'est pas à la taille cible, elle est redimensionnée avec LANCZOS.
    """
    if image.size != (target_width, target_height):
        image = image.resize((target_width, target_height), Image.LANCZOS)
    data = np.frombuffer(image.convert('RGB').tobytes(), dtype=np.uint8)
    data = data.reshape((target_height, target_width, 3))
    R5 = (data[..., 0] >> 3).astype(np.uint16) << 11
    G6 = (data[..., 1] >> 2).astype(np.uint16) << 5
    B5 = (data[..., 2] >> 3).astype(np.uint16)
    rgb565 = R5 | G6 | B5
    return np.ctypeslib.as_ctypes(rgb565.flatten())

# Pour la conversion en GIF, on conserve vos fonctions d'origine
def convert_image_to_gif(image_path, width, height):
    ensure_cache_dir()
    image = Image.open(image_path).convert("RGBA")

    # Si le fichier est exactement "_cache_dmd.png", utiliser resize_and_crop, sinon resize_and_pad
    if os.path.basename(image_path) == "_cache_dmd.png":
        image = resize_and_crop(image, width, height)
    else:
        image = resize_and_pad(image, width, height, True, True)

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
        nskip = max(1, int(fps // 5))
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
        while True:
            try:
                os.rename(gif_temp_path, gif_final_path)
                print(f"Renamed temporary GIF to final GIF: {gif_final_path}")
                break
            except PermissionError:
                print(f"PermissionError: Retrying renaming {gif_temp_path} to {gif_final_path}")
                time.sleep(1)
        if os.path.exists(gif_temp_path):
            os.remove(gif_temp_path)
            print(f"Removed temporary GIF: {gif_temp_path}")
        if os.path.exists(single_frame_gif_path):
            os.remove(single_frame_gif_path)
            print(f"Removed single-frame GIF: {gif_single_frame_path}")

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

# Déclaration de la classe ZeDMD via ctypes
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
    lib.ZeDMD_RenderRgb888.argtypes = [ZeDMD_ptr, ctypes.POINTER(ctypes.c_uint8)]
    lib.ZeDMD_RenderRgb565.argtypes = [ZeDMD_ptr, ctypes.POINTER(ctypes.c_uint16)]
    lib.ZeDMD_ClearScreen.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_SetFrameSize.argtypes = [ZeDMD_ptr, ctypes.c_uint16, ctypes.c_uint16]
    lib.ZeDMD_LedTest.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_EnableUpscaling.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_DisableUpscaling.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_EnableDebug.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_DisableDebug.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_SetUsbPackageSize.argtypes = [ZeDMD_ptr, ctypes.c_uint16]
    lib.ZeDMD_SetUdpDelay.argtypes = [ZeDMD_ptr, ctypes.c_uint8]
    lib.ZeDMD_SetPanelClockPhase.argtypes = [ZeDMD_ptr, ctypes.c_uint8]
    lib.ZeDMD_SetPanelI2sSpeed.argtypes = [ZeDMD_ptr, ctypes.c_uint8]
    lib.ZeDMD_SetPanelLatchBlanking.argtypes = [ZeDMD_ptr, ctypes.c_uint8]
    lib.ZeDMD_SetPanelMinRefreshRate.argtypes = [ZeDMD_ptr, ctypes.c_uint8]
    lib.ZeDMD_SetRGBOrder.argtypes = [ZeDMD_ptr, ctypes.c_uint8]
    lib.ZeDMD_SetBrightness.argtypes = [ZeDMD_ptr, ctypes.c_uint8]
    lib.ZeDMD_GetFirmwareVersion.argtypes = [ZeDMD_ptr]
    lib.ZeDMD_GetFirmwareVersion.restype = ctypes.c_char_p

    class ZeDMD:
        def __init__(self):
            self.obj = lib.ZeDMD_GetInstance()
            if not self.obj:
                print("Failed to get ZeDMD instance")
            else:
                print(f"ZeDMD instance created: {self.obj}")


        def open(self):
            if not self.obj:
                print("ZeDMD instance not initialized. Cannot open.")
                return False
            result = lib.ZeDMD_Open(self.obj)
            if result:
                # Dès que le DMD est ouvert, on configure le callback dans un thread séparé
                threading.Thread(target=lambda: lib.ZeDMD_SetLogCallback(self.obj, log_callback_c), daemon=True).start()
                print("Log callback set in thread.")
            return result

        def render_rgb24(self, frame):
            lib.ZeDMD_RenderRgb888(self.obj, frame)

        def render_rgb565(self, frame):
            lib.ZeDMD_RenderRgb565(self.obj, frame)

        def reset(self):
            lib.ZeDMD_Reset(self.obj)
            print("DMD reset executed.")

        def clear_screen(self):
            if not self.obj:
                print("ZeDMD instance not initialized. Cannot clear screen.")
                return
            lib.ZeDMD_ClearScreen(self.obj)

        def set_frame_size(self, width, height):
            lib.ZeDMD_SetFrameSize(self.obj, width, height)

        def get_firmware_version(self):
            return lib.ZeDMD_GetFirmwareVersion(self.obj).decode('utf-8')

        def led_test(self):
            lib.ZeDMD_LedTest(self.obj)

        def close(self):
            lib.ZeDMD_Close(self.obj)

        def enable_upscaling(self):
            lib.ZeDMD_EnableUpscaling(self.obj)

        def disable_upscaling(self):
            lib.ZeDMD_DisableUpscaling(self.obj)

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

        def set_usb_package_size(self, size):
            lib.ZeDMD_SetUsbPackageSize(self.obj, ctypes.c_uint16(size))
            print(f"USB package size set to {size}")

        def set_udp_delay(self, delay):
            lib.ZeDMD_SetUdpDelay(self.obj, ctypes.c_uint8(delay))
            print(f"UDP delay set to {delay}")

        def set_panel_clock_phase(self, phase):
            lib.ZeDMD_SetPanelClockPhase(self.obj, ctypes.c_uint8(phase))
            print(f"Panel clock phase set to {phase}")

        def set_panel_i2s_speed(self, speed):
            lib.ZeDMD_SetPanelI2sSpeed(self.obj, ctypes.c_uint8(speed))
            print(f"Panel I2S speed set to {speed}")

        def set_panel_latch_blankting(self, blanking):
            lib.ZeDMD_SetPanelLatchBlanking(self.obj, ctypes.c_uint8(blanking))
            print(f"Panel latch blanking set to {blanking}")

        def set_panel_min_refresh_rate(self, rate):
            lib.ZeDMD_SetPanelMinRefreshRate(self.obj, ctypes.c_uint8(rate))
            print(f"Panel minimum refresh rate set to {rate}")

        def set_rgb_order(self, order):
            lib.ZeDMD_SetRGBOrder(self.obj, ctypes.c_uint8(order))
            print(f"RGB order set to {order}")

        def set_brightness(self, brightness):
            lib.ZeDMD_SetBrightness(self.obj, ctypes.c_uint8(brightness))
            print(f"Brightness set to {brightness}")

        def set_transport(self, transport):
            lib.ZeDMD_SetTransport(self.obj, ctypes.c_uint8(transport))
            print(f"Transport set to {transport}")

        def set_y_offset(self, offset):
            lib.ZeDMD_SetYOffset(self.obj, ctypes.c_uint8(offset))
            print(f"Y offset set to {offset}")

        def set_panel_driver(self, driver):
            lib.ZeDMD_SetPanelDriver(self.obj, ctypes.c_uint8(driver))
            print(f"Panel driver set to {driver}")

    class DMDServer:
        def __init__(self, pipe_name):
            self.pipe_name = pipe_name
            self.zedmd = ZeDMD()
            self.zedmd_open = False
            self.last_image = None
            self.last_width = None
            self.last_height = None
            self.gif_durations = []
            self.cached_rgb565_frames = []
            self.gif_lock = threading.Lock()
            self.stop_event = threading.Event()
            self.image_queue = Queue(maxsize=2)
            self.pipe = None
            self.keep_alive_thread = None
            self.process_image_thread = None
            self.conversion_locks = {}
            self.last_request_time = time.time()
            self.last_client_activity = time.time()
            self.last_request = None
            self.display_count = 0
            self.animation_thread = None
            # Cache en mémoire pour les conversions afin d'éviter des accès disque répétés
            self.image_cache = {}  # {image_path: (buffers, durations)}

        def start(self):
            print("Starting server...")
            port, baudrate, width, height = self.detect_dmd_size()
            if port and baudrate and width and height:
                print("Opening ZeDMD...")
                if self.zedmd.open():
                    self.zedmd_open = True
                    self.zedmd.clear_screen()

                    usb_packet_size = 1024 if width == 256 else 512
                    self.zedmd.set_usb_package_size(usb_packet_size)
                    self.zedmd.set_udp_delay(0)
                    self.zedmd.set_panel_clock_phase(1)
                    self.zedmd.set_panel_i2s_speed(20)
                    self.zedmd.set_panel_latch_blankting(0)
                    refresh_rate = 60 if width == 256 else 90
                    self.zedmd.set_panel_min_refresh_rate(refresh_rate)
                    self.zedmd.set_transport(0)
                    self.zedmd.set_panel_driver(0)
                    # Optionnel: si le DMD supporte l'upscaling, activez-le
                    self.zedmd.enable_upscaling()

                    self.zedmd.set_rgb_order(5)
                    self.zedmd.set_brightness(2)
                    self.zedmd.set_y_offset(0)
                    self.last_request_time = time.time()
                    self.last_client_activity = time.time()
                    self.display_count = 0
                    print("ZeDMD opened and configured successfully.")

                    if self.last_image and self.last_width and self.last_height:
                        self.display_image(self.last_image, self.last_width, self.last_height)
                    else:
                        self.display_image('images/default.png', width, height)
                else:
                    print("Failed to open ZeDMD")
                    return

        def listen_for_clients(self):
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
                    if e.winerror == 231:
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
                        self.last_client_activity = time.time()
                        self.last_request = (image_path, width, height)
                        try:
                            self.image_queue.put_nowait((image_path, width, height))
                        except Full:
                            pass
                        print(f"Queue state after adding: {[item for item in self.image_queue.queue]} at {time.time()}")
            except pywintypes.error as e:
                if e.winerror != 109:
                    print(f"Error handling client: {e}")
            finally:
                win32file.CloseHandle(pipe)

        def process_image_queue(self):
            while True:
                try:
                    image_path, width, height = self.image_queue.get(timeout=1)
                    if self.image_queue.empty():
                        self.display_image(image_path, width, height)
                    print(f"Queue state after removing: {[item for item in self.image_queue.queue]} at {time.time()}")
                except Empty:
                    pass

        def display_image(self, image_path, width, height):
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
            start_time = time.time()
            self.zedmd.set_frame_size(width, height)

            # Vérifier le cache en mémoire, mais invalider si le fichier a été modifié
            update_cache = False
            if image_path in self.image_cache:
                cache_entry = self.image_cache[image_path]
                cached_mtime = cache_entry.get('mtime', 0)
                current_mtime = os.path.getmtime(image_path)
                if current_mtime > cached_mtime:
                    print(f"File {image_path} updated on disk; updating in-memory cache.")
                    update_cache = True
                else:
                    print(f"Using in-memory cache for {image_path}")
                    buffers, durations = cache_entry['buffers'], cache_entry['durations']
            else:
                update_cache = True

            if update_cache:
                base = os.path.splitext(os.path.basename(image_path))[0]
                gif_single_frame_path = os.path.join(CACHE_DIR, f"{base}_single_frame.gif")
                gif_final_path = os.path.join(CACHE_DIR, f"{base}.gif")
                gif_temp_path = os.path.join(CACHE_DIR, f"{base}_temp.gif")
                conversion_lock = self.conversion_locks.setdefault(image_path, threading.Lock())
                # Intégration du bloc oublié :
                if image_path.lower().endswith('.gif'):
                    gif_path = image_path
                else:
                    if os.path.exists(gif_final_path):
                        print(f"Loading GIF from cache: {gif_final_path}")
                        gif_path = gif_final_path
                    elif os.path.exists(gif_single_frame_path):
                        print(f"Single-frame GIF already exists: {gif_single_frame_path}")
                        gif_path = gif_single_frame_path
                        if not os.path.exists(gif_final_path) and not os.path.exists(gif_temp_path):
                            print(f"Launching background conversion of MP4 to GIF: {image_path}")
                            threading.Thread(target=convert_mp4_to_gif,
                                             args=(image_path, width, height, gif_temp_path, gif_final_path, gif_single_frame_path, conversion_lock),
                                             daemon=True).start()
                    else:
                        if image_path.lower().endswith('.mp4'):
                            print(f"Creating single-frame GIF from MP4: {image_path}")
                            create_single_frame_gif(image_path, width, height, gif_single_frame_path)
                            gif_path = gif_single_frame_path
                            if not os.path.exists(gif_final_path) and not os.path.exists(gif_temp_path):
                                print(f"Launching background conversion of MP4 to GIF: {image_path}")
                                threading.Thread(target=convert_mp4_to_gif,
                                                 args=(image_path, width, height, gif_temp_path, gif_final_path, gif_single_frame_path, conversion_lock),
                                                 daemon=True).start()
                        else:
                            print(f"Converting image to GIF: {image_path}")
                            gif_path = convert_image_to_gif(image_path, width, height)
                if not os.path.exists(gif_path):
                    print(f"File {gif_path} does not exist.")
                    return
                try:
                    with Image.open(gif_path) as img:
                        frames = []
                        durations = []
                        for frame in ImageSequence.Iterator(img):
                            frames.append(frame.copy())
                            durations.append(frame.info.get('duration', 100) / 1000.0)
                except Exception as e:
                    print(f"Error opening {gif_path}: {e}")
                    return
                if len(frames) == 0:
                    print("No frames found in GIF, cannot display.")
                    return
                buffers = [image_to_rgb565_array(frame, width, height) for frame in frames]
                # Mettre à jour le cache en mémoire avec le temps de modification actuel
                self.image_cache[image_path] = {
                    'buffers': buffers,
                    'durations': durations,
                    'mtime': os.path.getmtime(image_path)
                }

            # Utiliser buffers et durations (cas statique ou animé)
            if len(buffers) == 1:
                # Cas d'une image statique
                new_frame = buffers[0]
                curr = np.ctypeslib.as_array(new_frame)
                if hasattr(self, "previous_frame") and np.array_equal(self.previous_frame, curr):
                    print("No change detected in static frame; skipping update.")
                    return
                else:
                    print("Static frame changed; updating display.")
                self.previous_frame = np.copy(curr)
                self.zedmd.render_rgb565(new_frame)
            else:
                print("Animated GIF detected; starting animation thread.")
                # Arrêter l'animation en cours (si existante) avant de lancer la nouvelle
                self.stop_animation()
                with self.gif_lock:
                    self.cached_rgb565_frames = buffers
                    self.gif_durations = durations
                    self.stop_event.clear()  # Réinitialiser le flag d'arrêt pour la nouvelle animation
                # Lancer le nouveau thread et conserver sa référence
                self.animation_thread = threading.Thread(target=self.play_gif, args=(width, height), daemon=True)
                self.animation_thread.start()

            end_time = time.time()
            print(f"Time to process image: {end_time - start_time:.6f} seconds")


        def play_gif(self, width, height):
            with self.gif_lock:
                frames = self.cached_rgb565_frames[:]
                durations = self.gif_durations[:]  # Durées en secondes
            if not frames or not durations:
                print("No frames available for animation.")
                return

            total_duration = sum(durations)
            start_time = time.perf_counter()
            virtual_time = 0  # Temps théorique cumulé depuis le début de l'animation
            current_frame = 0

            # Seuil en secondes pour décider de sauter des frames
            skip_threshold = 0.1

            while self.zedmd_open and frames and not self.stop_event.is_set():
                # Ajouter la durée de la frame actuelle à la timeline théorique
                frame_duration = durations[current_frame]
                virtual_time += frame_duration

                # Rendu de la frame et mesure du temps de rendu
                render_start = time.perf_counter()
                self.zedmd.render_rgb565(frames[current_frame])
                render_time = time.perf_counter() - render_start

                # Calcul du moment théorique auquel la frame aurait dû être affichée
                expected_time = start_time + virtual_time
                now = time.perf_counter()
                delay = expected_time - now

                if delay > 0:
                    # On attend le temps restant pour respecter le timing prévu
                    time.sleep(delay)
                else:
                    # Si le retard est significatif, on saute des frames pour rattraper
                    if abs(delay) >= skip_threshold:
                        while delay < -skip_threshold and not self.stop_event.is_set():
                            print(f"Skipping frame {current_frame} to catch up (delay={-delay:.3f}s)")
                            current_frame = (current_frame + 1) % len(frames)
                            virtual_time += durations[current_frame]
                            expected_time = start_time + virtual_time
                            now = time.perf_counter()
                            delay = expected_time - now
                            # Si on a parcouru une boucle complète, on réinitialise la timeline
                            if virtual_time >= total_duration:
                                start_time = time.perf_counter()
                                virtual_time = 0
                                break
                # Passage à la frame suivante pour le cycle normal
                current_frame = (current_frame + 1) % len(frames)



        def stop_animation(self):
            self.stop_event.set()  # Signaler l'arrêt de l'animation
            if self.animation_thread is not None:
                self.animation_thread.join()  # Attendre la fin du thread d'animation
                self.animation_thread = None
            with self.gif_lock:
                self.cached_rgb565_frames = []
                self.gif_durations = []
            # Réinitialiser previous_frame afin que l'image statique soit toujours affichée
            self.previous_frame = None

        def keep_dmd_alive(self):
            while True:
                current_time = time.time()
                elapsed_time = current_time - self.last_client_activity
                print(f"### Keep_dmd_alive - Elapsed time since last client activity: {elapsed_time:.2f} seconds")
                if elapsed_time >= 1200:
                    print("Restarting DMDServer due to inactivity...")
                    self.stop_animation()
                    self.close()
                    print("Server closed. Restarting...")
                    self.start()
                    print("Server restarted and last image displayed.")
                if not self.stop_event.is_set() and self.cached_rgb565_frames:
                    print("GIF animation is currently playing.")
                else:
                    if self.last_image and self.last_width and self.last_height:
                        self.display_image(self.last_image, self.last_width, self.last_height)
                time.sleep(10)

        def detect_dmd_size(self):
            port, baudrate, width, height = detect_com_ports_and_baudrates()
            if port and baudrate:
                return port, baudrate, width, height
            return None, None, None, None

        def close(self):
            print("Close server starting...")
            if self.zedmd_open:
                print("zedmd close")
                self.zedmd.close()
                self.zedmd_open = False

    if __name__ == "__main__":
        ensure_cache_dir()
        load_config()
        server = DMDServer(r'\\.\pipe\dmd-pipe')
        try:
            server.zedmd.enable_debug()  # Activer le mode débogage si besoin
            server.start()
            threading.Thread(target=server.keep_dmd_alive, daemon=True).start()

            # Lancer la surveillance du fichier si ActiveDMD est activé dans la config
            if config['Settings'].getboolean('ActiveDMD'):
                marquee_image_path = os.path.join(CACHE_DIR, '_cache_dmd.png')
                logging.info(f"Surveillance activée pour {marquee_image_path}")
                event_handler = FileWatcher(marquee_image_path, on_file_modified)
                observer = Observer()
                observer.schedule(event_handler, path=os.path.dirname(marquee_image_path), recursive=False)
                observer.start()
            else:
                logging.info("La surveillance de ActiveDMD est désactivée dans la config.")

            server.listen_for_clients()
        except KeyboardInterrupt:
            print("Shutting down server...")
            server.close()
        finally:
            try:
                observer.stop()
                observer.join()
            except Exception:
                pass
            #server.zedmd.disable_debug()  # Désactiver le mode débogage si besoin
