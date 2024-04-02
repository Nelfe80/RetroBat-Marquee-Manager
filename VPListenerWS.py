import configparser
import asyncio
import websockets
import subprocess
import logging
import struct
from PIL import Image, ImageDraw
import os
import time
import numpy as np

logging.basicConfig(level=logging.DEBUG)
creation_flags = 0
# Configuration
WEBSOCKET_HOST = '127.0.0.1'
WEBSOCKET_PORT = 80

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

last_execution_time = 0
def decode_image_data(message, image_width, image_height):
    global last_execution_time
    # Obtenir le temps actuel
    current_time = time.time()
    # Vérifier si la dernière exécution a eu lieu il y a moins d'une seconde
    if current_time - last_execution_time < 0.2:
        return "Demande ignorée, car appelée trop fréquemment."
    # La suite du traitement comme avant
    image_data = message
    img = convert_binary_to_image(image_data, image_width, image_height)
    update_imagedmd(img)
    # Mise à jour du temps de la dernière exécution
    last_execution_time = current_time
    return f"Image pushed"

def decode_gray2planes_message(message, width, height):
    global last_execution_time
    # Obtenir le temps actuel
    current_time = time.time()
    # Vérifier si la dernière exécution a eu lieu il y a moins d'une seconde
    if current_time - last_execution_time < 0.1:
        return "Demande ignorée, car appelée trop fréquemment."
    planes = message[4:]  # Ajustez selon la structure exacte du message
    buffer = join_planes(2, planes, width, height)

    colored_buffer = bytearray()
    for gray_value in buffer:
        color = apply_color_to_gray_scale(gray_value, 2)  # 2 bits pour gray2Planes
        colored_buffer.extend(color)

    img = Image.frombytes('RGB', (width, height), bytes(colored_buffer))

    update_imagedmd(img)
    # Mise à jour du temps de la dernière exécution
    last_execution_time = current_time
    return img

def decode_gray4planes_message(message, width, height):
    global last_execution_time, current_color, gray2_palette, gray4_palette
    # Obtenir le temps actuel
    current_time = time.time()
    # Vérifier si la dernière exécution a eu lieu il y a moins d'une seconde
    if current_time - last_execution_time < 0.1:
        return "Demande ignorée, car appelée trop fréquemment."
    planes = message[4:]  # Ajustez selon la structure exacte du message
    buffer = join_planes(4, planes, width, height)

    colored_buffer = bytearray()
    for gray_value in buffer:
        color = apply_color_to_gray_scale(gray_value, 4)  # 4 bits pour gray4Planes
        colored_buffer.extend(color)

    img = Image.frombytes('RGB', (width, height), bytes(colored_buffer))

    update_imagedmd(img)
    # Mise à jour du temps de la dernière exécution
    last_execution_time = current_time
    return img

import ctypes

def join_planes(bitlength, planes, width, height):
    frame = bytearray(width * height)
    plane_size = len(planes) // bitlength

    # Définir des types d'entiers signés de 32 bits pour byte_pos et bit_pos
    c_int32 = ctypes.c_int32
    byte_pos = c_int32(0)
    bit_pos = c_int32(0)

    # Calculer l'offset de base pour déplacer le début de la frame vers la gauche
    base_offset = width-20

    for byte_pos.value in range(width * height // 8):
        for bit_pos.value in range(7, -1, -1):
            for plane_pos in range(bitlength):
                bit = 1 if is_bit_set(planes[plane_size * plane_pos + byte_pos.value], bit_pos.value) else 0

                # Calculer le décalage en fonction de la position du plan et du décalage relatif
                offset = (bitlength - plane_pos) * 3 + plane_pos * 27 - base_offset

                frame_index = (byte_pos.value * 8 + bit_pos.value) + offset
                if 0 <= frame_index < len(frame):
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

def apply_dmd_effect(img, target_width, target_height):
    # Convertir l'image PIL en tableau Numpy
    img_array = np.array(img)

    # Calculer le facteur d'échelle et les nouvelles dimensions
    scale_x = target_width // img.width
    scale_y = target_height // img.height
    scale_factor = min(scale_x, scale_y)

    # Calculer le dot_size en fonction de l'échelle
    dot_size = max(1, scale_factor)

    # Réduire légèrement le diamètre de chaque dot
    dot_diameter = dot_size - 2

    # Nouvelles dimensions ajustées en fonction du dot_size
    new_width = img.width * dot_size
    new_height = img.height * dot_size

    # Créer une nouvelle image avec des bords noirs
    dmd_img = Image.new('RGB', (target_width, target_height), (0, 0, 0))
    draw = ImageDraw.Draw(dmd_img)

    # Parcourir chaque pixel de l'image et dessiner un dot
    for y in range(img.height):
        for x in range(img.width):
            dot_x = (target_width - new_width) // 2 + x * dot_size
            dot_y = (target_height - new_height) // 2 + y * dot_size
            color = tuple(img_array[y, x])
            draw.ellipse([dot_x, dot_y, dot_x + dot_diameter, dot_y + dot_diameter], fill=color)

    return dmd_img

def update_imagedmd(img):
    #dmd_img = img
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

if __name__ == '__main__':
    load_config()
    if not os.path.exists('images'):
        os.makedirs('images')
    # Démarrer le serveur WebSocket
    start_server = websockets.serve(handler, WEBSOCKET_HOST, WEBSOCKET_PORT)
    ensure_mpv_running()

    # Exécuter le serveur
    asyncio.get_event_loop().run_until_complete(start_server)
    asyncio.get_event_loop().run_forever()








