import configparser
import asyncio
import websockets
import subprocess
import logging
import struct
from PIL import Image, ImageDraw
import os
import time
from time import perf_counter
import numpy as np
from collections import deque

logging.basicConfig(level=logging.DEBUG)

# Configuration
creation_flags = 0

WEBSOCKET_HOST = '127.0.0.1'
WEBSOCKET_PORT = 80

FRAME_QUEUE_SIZE = 30
frame_queue = deque(maxlen=FRAME_QUEUE_SIZE)

global_image_width = 128  # Valeurs par défaut
global_image_height = 32
default_color = 0xec843d  # Couleur par défaut
current_color = default_color
gray2_palette = None
gray4_palette = None

def decode_binary_message(message):
    global global_image_width, global_image_height
    try:
        if message.startswith(b'gameName'):
            game_name = message[9:].split(b'\x00', 1)[0].decode('utf-8')
            return f'Type: gameName, Game Name: {game_name}'

        elif message.startswith(b'dimensions'):
            _, width, height = struct.unpack('<11sII', message)
            global_image_width, global_image_height = width, height
            return f'Type: dimensions, Dimensions: {width}x{height}'

        elif message.startswith(b'color'):
            return decode_color_message(message)

        elif message.startswith(b'clearColor'):
            return clear_color(message)

        elif message.startswith(b'clearPalette'):
            clear_palette()
            return 'Type: clearPalette'

        elif message.startswith(b'rgb24'):  # ou un autre identifiant unique pour ces données
            return decode_image_data(message, global_image_width, global_image_height)

        elif message.startswith(b'gray4Planes'):
            return decode_gray4planes_message(message, global_image_width, global_image_height)

        elif message.startswith(b'gray2Planes'):
            return decode_gray2planes_message(message, global_image_width, global_image_height)

        else:
            return 'Unknown binary format'

    except Exception as e:
        logging.error(f"Error decoding message: {e}")
        return None

def decode_color_message(message):
    global current_color
    _, color_value = struct.unpack('<6sI', message)
    current_color = color_value
    return f'Type: color, Color: {current_color:#08x}'

def clear_color():
    global current_color
    current_color = default_color

def clear_palette():
    global gray2_palette, gray4_palette
    gray2_palette = None
    gray4_palette = None

def decode_image_data(message, image_width, image_height):
    image_data = message
    img = convert_binary_to_image(image_data, image_width, image_height)
    timestamp = perf_counter()
    frame_queue.append((img, timestamp))
    return f"Image pushed"

def decode_gray4planes_message(message, width, height):
    planes = message[4:]  # Ajustez selon la structure exacte du message
    buffer = join_planes(4, planes, width, height)

    colored_buffer = bytearray()
    for gray_value in buffer:
        color = apply_color_to_gray_scale(gray_value, 4)  # 4 bits pour gray4Planes
        colored_buffer.extend(color)

    img = Image.frombytes('RGB', (width, height), bytes(colored_buffer))
    timestamp = perf_counter()
    frame_queue.append((img, timestamp))
    return img

def decode_gray2planes_message(message, width, height):
    print(f"### Received gray2Planes message of length {len(message)}")
    print(f"### Message content: {message.hex()}")
    print(f"### Image dimensions: {width}x{height}")

    planes = message[4:]  # Ajustez selon la structure exacte du message
    buffer = join_planes(2, planes, width, height)

    colored_buffer = bytearray()
    for gray_value in buffer:
        color = apply_color_to_gray_scale(gray_value, 2)  # 2 bits pour gray2Planes
        colored_buffer.extend(color)

    img = Image.frombytes('RGB', (width, height), bytes(colored_buffer))
    timestamp = perf_counter()
    frame_queue.append((img, timestamp))
    return img

def join_planes(bitlength, planes, width, height):
    frame = bytearray(width * height)
    plane_size = len(planes) // bitlength

    # Calculer l'offset de base pour déplacer le début de la frame vers la gauche
    if bitlength == 2:
        base_offset = width - 26
    elif bitlength == 4:
        base_offset = width - 20
    else:
        raise ValueError("Invalid bitlength. Expected 2 or 4.")

    for byte_pos in range(width * height // 8):
        # Calculer le décalage en fonction de la position du plan et du décalage relatif
        offset = base_offset

        for bit_and_plane_pos in range(bitlength * 8 - 1, -1, -1):
            plane_pos = bit_and_plane_pos // 8
            bit_pos = bit_and_plane_pos % 8

            bit = 1 if is_bit_set(planes[plane_size * plane_pos + byte_pos], bit_pos) else 0
            if bitlength == 2:
                offset = (bitlength - plane_pos) * 3 + plane_pos * 51 - base_offset
            elif bitlength == 4:
                offset = (bitlength - plane_pos) * 3 + plane_pos * 27 - base_offset

            frame_index = (byte_pos * 8 + bit_pos) + offset
            #if 0 <= frame_index < len(frame):
            frame[frame_index] |= (bit << plane_pos)

    return frame

def is_bit_set(byte, pos):
    return (byte & (1 << pos)) != 0

def convert_binary_to_image(data, image_width, image_height):
    img = Image.frombytes('RGB', (image_width, image_height), data)
    r, g, b = img.split()  # Séparer les canaux
    img = Image.merge("RGB", (g, b, r))  # Réarranger si nécessaire

    # Découper les trois premières colonnes
    first_three_columns = img.crop((0, 0, 3, image_height))

    # Découper le reste de l'image
    rest_of_image = img.crop((3, 0, image_width, image_height))

    # Corriger les couleurs des trois premiers points supérieurs
    for x in range(3):
        # Utiliser la couleur du point juste en dessous
        below_color = first_three_columns.getpixel((x, 1))
        first_three_columns.putpixel((x, 0), below_color)

    # Coller le reste de l'image en premier
    new_img = Image.new('RGB', (image_width, image_height))
    new_img.paste(rest_of_image, (0, 0))

    # Coller les trois premières colonnes en dernier
    new_img.paste(first_three_columns, (image_width - 3, 0))

    return new_img

def apply_color_to_gray_scale(value, bit_depth):
    global current_color, gray2_palette, gray4_palette

    if bit_depth == 2 and gray2_palette:
        color = gray2_palette[value]
    elif bit_depth == 4 and gray4_palette:
        color = gray4_palette[value]
    else:
        intensity = value / (2 ** bit_depth - 1)
        red = (current_color >> 16) & 0xFF
        green = (current_color >> 8) & 0xFF
        blue = current_color & 0xFF
        color = (int(red * intensity), int(green * intensity), int(blue * intensity))

    return color

def draw_ellipse(draw, position, color, diameter):
    x, y = position
    draw.ellipse([x, y, x + diameter, y + diameter], fill=color)

def apply_dmd_effect(img, target_width, target_height):
    img_array = np.array(img)

    scale_x = target_width // img.width
    scale_y = target_height // img.height
    scale_factor = min(scale_x, scale_y)

    dot_size = max(1, scale_factor)
    dot_diameter = dot_size - 2

    new_width = img.width * dot_size
    new_height = img.height * dot_size

    dmd_img = Image.new('RGB', (target_width, target_height), (0, 0, 0))
    draw = ImageDraw.Draw(dmd_img)

    for y in range(img.height):
        for x in range(img.width):
            dot_x = (target_width - new_width) // 2 + x * dot_size
            dot_y = (target_height - new_height) // 2 + y * dot_size
            color = img_array[y, x]
            draw_ellipse(draw, (dot_x, dot_y), tuple(color), dot_diameter)

    return dmd_img

def update_imagedmd(img):
    dmd_img = apply_dmd_effect(img, int(config['Settings']['MarqueeWidth']), int(config['Settings']['MarqueeHeight']))
    filename = f"images/image_dmd.png"
    dmd_img.save(filename)
    command = config['Commands']['game-forceupdate'].format(
       marquee_file=filename,
       IPCChannel=config['Settings']['IPCChannel']
    )
    logging.info(f"Executing the command : {command}")
    subprocess.run(command, shell=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)
    return True

async def handler(websocket, path):
    global last_frame_time
    if path != "/dmd":
        return

    try:
        async for message in websocket:
            if isinstance(message, str):
                logging.debug(f"Text message received: {message}")
                continue

            decoded_message = decode_binary_message(message)
            logging.debug(f"Decoded_message: {decoded_message}")

    except Exception as e:
        logging.error(f"WebSocket error: {e}")

def launch_media_player():
    kill_command = config['Settings']['MPVKillCommand']
    subprocess.run(kill_command, shell=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)
    logging.info(f"Execute kill command : {kill_command}")

    launch_command = config['Settings']['MPVLaunchCommand'].format(
        MPVPath=config['Settings']['MPVPath'],
        IPCChannel=config['Settings']['IPCChannel'],
        ScreenNumber=config['Settings'].get('ScreenNumber', '1'),
        DefaultImagePath=config['Settings']['DefaultImagePath'],
        MarqueeBackgroundCodeColor=config['Settings']['MarqueeBackgroundCodeColor']
    )
    subprocess.Popen(launch_command, shell=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)
    logging.info(f"MPV launch command executed : {launch_command}")

def is_mpv_running():
    try:
        test_command = config['Settings']['MPVTestCommand'].format(IPCChannel=config['Settings']['IPCChannel'])
        subprocess.run(test_command, shell=True, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)
        logging.info(f"MPV is currently running.")
        return True
    except subprocess.CalledProcessError:
        logging.info(f"MPV is not currently running.")
        return False

def ensure_mpv_running():
    if not is_mpv_running():
        launch_media_player()


def grayto_rgb24(buffer, width, height, num_colors):
    rgb_frame = bytearray(width * height * 3)
    pos = 0
    for y in range(height):
        for x in range(width):
            # Ici, nous calculons la luminosité basée sur la valeur en niveaux de gris
            # Cette partie doit être ajustée en fonction de votre palette ou de votre méthode de conversion
            lum = buffer[y * width + x] / num_colors
            rgb_val = int(lum * 255)
            rgb_frame[pos:pos+3] = [rgb_val, rgb_val, rgb_val]  # Pour une image en niveaux de gris
            pos += 3
    return rgb_frame

#def convert_binary_to_image(data, image_width, image_height):
#    img = Image.frombytes('RGB', (image_width, image_height), data)
#    return img

def decode_binary_data(data):
    # Dépaqueter les données binaires en une liste d'entiers de 3 octets
    pixels = struct.unpack('<' + 'B' * (len(data) // 3), data)

    # Créer une image à partir des données dépaquetées
    img = Image.new('RGB', (128, 32))
    img.putdata(pixels)
    return img


config = configparser.ConfigParser()
def load_config():
    global config
    config.read('config.ini')

    current_working_dir = os.getcwd()  # C:\RetroBat\plugins\MarqueeManager\
    def update_path(setting, default_path):
        logging.info(f"update_path {setting} {default_path}")
        # Vérifier si la variable n'existe pas ou n'est pas un lien absolu
        if setting not in config['Settings'] or not os.path.isabs(config['Settings'].get(setting, '')):
            config['Settings'][setting] = default_path

    logging.info(f"{current_working_dir}")
    if config['Settings']['logFile'] == "true":
        logging.getLogger('werkzeug').setLevel(logging.INFO)
        logging.info("Start logging")

    update_path('RetroBatPath', os.path.dirname(os.path.dirname(current_working_dir)))
    update_path('RomsPath', os.path.join(config['Settings']['RetroBatPath'], 'roms'))
    update_path('DefaultImagePath', os.path.join(current_working_dir, 'images', 'default.png'))
    update_path('MarqueeImagePath', os.path.join(current_working_dir, 'images'))
    update_path('MarqueeImagePathDefault', os.path.join(config['Settings']['RetroBatPath'], 'roms'))
    update_path('FanartMarqueePath', os.path.join(config['Settings']['RetroBatPath'], 'roms'))
    update_path('SystemMarqueePath', os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'themes', 'es-theme-carbon-master', 'art', 'logos'))
    update_path('CollectionMarqueePath', os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'themes', 'es-theme-carbon-master', 'art', 'logos'))
    update_path('MPVPath', os.path.join(current_working_dir, 'mpv', 'mpv.exe'))
    update_path('IMPath', os.path.join(current_working_dir, 'imagemagick', 'convert.exe'))

FRAMERATE = 30
async def main_loop():
    global last_update_time
    last_update_time = perf_counter()

    while True:
        current_time = perf_counter()

        if len(frame_queue) > 0:
            #img, img_timestamp = frame_queue.popleft()
            img, img_timestamp = frame_queue[-1]
            # Calculer le temps écoulé depuis la capture de l'image
            time_since_img_captured = current_time - img_timestamp

            # Vérifier si l'image est toujours dans le cadre du framerate ou si c'est la derniere frame
            if time_since_img_captured <= 1 / FRAMERATE or len(frame_queue) == 0:

                update_imagedmd(img)

                # Mise à jour de last_update_time
                last_update_time = current_time

            # Calculer le délai nécessaire pour maintenir le framerate
            time_since_last_update = current_time - last_update_time
            delay = max(0, (1 / FRAMERATE) - time_since_last_update)
            await asyncio.sleep(delay)
        else:
            await asyncio.sleep(0.01)  # Petit délai pour éviter de surcharger la boucle


if __name__ == '__main__':
    load_config()
    if not os.path.exists('images'):
        os.makedirs('images')
    # Démarrer le serveur WebSocket
    start_server = websockets.serve(handler, WEBSOCKET_HOST, WEBSOCKET_PORT)
    ensure_mpv_running()

    # Ajouter la tâche asynchrone à la boucle d'événements
    asyncio.get_event_loop().create_task(main_loop())

    # Exécuter le serveur
    asyncio.get_event_loop().run_until_complete(start_server)
    asyncio.get_event_loop().run_forever()










