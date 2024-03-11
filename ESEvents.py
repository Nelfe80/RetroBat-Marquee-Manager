from flask import Flask, request
import configparser
import subprocess
import os
import json
import urllib.parse
import shlex
import xml.etree.ElementTree as ET
import logging

app = Flask(__name__)
creation_flags = 0
if sys.platform == "win32":  # Uniquement pour Windows
    creation_flags = subprocess.CREATE_NO_WINDOW

config = configparser.ConfigParser()
def load_config():
    global config
    config.read('config.ini')
    if config['Settings']['logFile'] == "true":
        #logging.basicConfig(level=logging.INFO)
        logging.basicConfig(filename="ESEvents.log", level=logging.INFO)
        logging.getLogger('werkzeug').setLevel(logging.INFO)
        logging.info("Start logging")

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
    update_path('SystemMarqueePath', os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'themes', 'es-theme-carbon-master', 'art', 'logos'))
    update_path('CollectionMarqueePath', os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'themes', 'es-theme-carbon-master', 'art', 'logos'))
    update_path('MPVPath', os.path.join(current_working_dir, 'mpv', 'mpv.exe'))
    update_path('IMPath', os.path.join(current_working_dir, 'imagemagick', 'convert.exe'))

def load_systems_config(xml_relative_path):
    script_dir = os.path.dirname(os.path.realpath(__file__))
    xml_path = os.path.join(script_dir, xml_relative_path)
    tree = ET.parse(xml_path)
    root = tree.getroot()

    system_folders = {}

    for system in root.findall('system'):
        if system.find('name') is not None and system.find('path') is not None:
            name = system.find('name').text
            path = system.find('path').text
            theme = system.find('theme').text
            # Recherche du nom du dossier des roms
            roms_path = config['Settings']['RomsPath']
            folder_rom_name = os.path.basename(os.path.normpath(path.strip('~\\..')))
            system_folders[name] = folder_rom_name
            system_folders[name+".theme"] = theme
            #logging.info(f"System {name} loading folder_rom_name - {folder_rom_name} path {path} - theme : {system_folders[name+'.theme']}")

        else:
            logging.info(f"Missing name and/or path in es_systems.cfg for the system {system.tag}")

    return system_folders

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

def escape_file_path(path):
    return shlex.quote(path)

def parse_collection_correlation():
    correlations = config['Settings']['CollectionCorrelation']
    correlation_dict = {}
    for pair in correlations.split(','):
        key, value = pair.strip().split(':')
        correlation_dict[key.strip()] = value.strip()
    return correlation_dict

def find_marquee_for_collection(game_name):
    collection_correlation = parse_collection_correlation()
    collection_marquee_path = config['Settings']['CollectionMarqueePath']
    alternative_names = config['Settings']['CollectionAlternativNames'].split(',')
    lng = config['Settings']['Language']

    # Remplacer game_name si dans les corrélations
    correlated_name = collection_correlation.get(game_name, game_name)

    # Créer une liste de tous les noms possibles pour le marquee
    marquee_names = [f"{correlated_name}-{lng}", correlated_name, f"{game_name}-{lng}", game_name]
    for alt in alternative_names:
        alt = alt.strip()
        alt_replaced = collection_correlation.get(alt, alt)
        marquee_names.extend([f"{alt_replaced}{game_name}-{lng}", f"{alt_replaced}{game_name}", f"{game_name}{alt_replaced}-{lng}", f"{game_name}{alt_replaced}"])
        marquee_names.extend([f"{alt}{game_name}-{lng}", f"{alt}{game_name}", f"{game_name}{alt}-{lng}", f"{game_name}{alt}"])
    for name in marquee_names:
        # Essayer d'abord avec le chemin formaté
        formatted_path = os.path.join(collection_marquee_path, config['Settings']['CollectionFilePath'].format(collection_name=name))
        marquee_file = find_file(formatted_path)
        if marquee_file is not None:
            return marquee_file

        # Essayer ensuite avec le nom direct
        direct_path = os.path.join(collection_marquee_path, name)
        marquee_file = find_file(direct_path)
        if marquee_file is not None:
            return marquee_file

    # Si aucun marquee n'a été trouvé
    return None

def find_system_marquee(system_name, folder_rom_name, systems_config, config):
    marquee_structure = config['Settings']['SystemFilePath']
    marquee_paths = [
        marquee_structure.format(system_name=folder_rom_name),
        marquee_structure.format(system_name=systems_config.get(system_name + ".theme"))
    ]

    for marquee_path in marquee_paths:
        if not system_name and not folder_rom_name and not marquee_path:
            marquee_path = 'retrobat'
        full_marquee_path = os.path.join(config['Settings']['SystemMarqueePath'], marquee_path)
        #logging.info(f"FMF System topper path (full_marquee_path) : {full_marquee_path}")

        marquee_file = find_file(full_marquee_path)
        if marquee_file:
            #logging.info(f"FMF System Topper : {marquee_file}")
            return marquee_file

    return None

def find_marquee_file(system_name, game_name, systems_config, game_title, rom_path):
    logging.info(f"FMF find_marquee_file : system_name : {system_name} - game_name : {game_name} - game_title : {game_title} - rom_path : {rom_path}")
    lng=config['Settings']['Language']
    folder_rom_name = systems_config.get(system_name, system_name)
    # Si le marquee est une collection
    if system_name == 'collection':
        marquee_file = find_marquee_for_collection(game_name)
        if marquee_file:
            #logging.info(f"FMF Found collection topper : {marquee_file}")
            return marquee_file
    # Si le marquee est un jeu
    # Test marquee custom
    marquee_structure = config['Settings']['MarqueeFilePath']
    marquee_path = marquee_structure.format(system_name=folder_rom_name, game_name=game_name)
    #logging.info(f"FMF GAME marquee_structure : {marquee_structure} system_name : {system_name} - game_name : {game_name} - folder_rom_name : {folder_rom_name} - marquee_path : {marquee_path}")
    full_marquee_path = os.path.join(config['Settings']['MarqueeImagePath'], marquee_path)
    #logging.info(f"FMF Full_marquee_path : {full_marquee_path}")
    marquee_file = find_file(full_marquee_path)
    # Test marquee default
    if marquee_file is None:
        marquee_structure_default = config['Settings']['MarqueeFilePathDefault']
        marquee_path_default = marquee_structure_default.format(system_name=folder_rom_name, game_name=game_name)
        full_marquee_path_default = os.path.join(config['Settings']['MarqueeImagePathDefault'], marquee_path_default)
        #logging.info(f"FMF Full_marquee_path_default : {full_marquee_path_default}")
        marquee_file = find_file(full_marquee_path_default)
        # Lancer le scraping si MarqueeAutoScraping est activé et si le marquee est trouvé avec le chemin par défaut
        if marquee_file and game_title is not None and game_title != '' and config['Settings']['MarqueeAutoScraping'] == "true":
            add_to_scrap_pool(system_name, game_title, game_name, marquee_path, full_marquee_path, rom_path)

    if marquee_file:
        #logging.info(f"FMF Found game topper : {marquee_file}")
        return marquee_file
    else:
        if game_title is not None and game_title != '' and config['Settings']['MarqueeAutoScraping'] == "true":
            add_to_scrap_pool(system_name, game_title, game_name, marquee_path, full_marquee_path, rom_path)
    # Si le marquee est un système
    marquee_file = find_system_marquee(system_name, folder_rom_name, systems_config, config)
    if marquee_file:
        #logging.info(f"FMF Found system topper : {marquee_file}")
        return marquee_file

    #logging.info(f"FMF Using the default image : {config['Settings']['DefaultImagePath']}")
    return config['Settings']['DefaultImagePath']

def convert_image(img_path, target_img_path):
    marquee_width = int(config['Settings']['MarqueeWidth'])
    marquee_height = int(config['Settings']['MarqueeHeight'])
    marquee_border = int(config['Settings']['MarqueeBorder'])

    # Déterminer si le fichier source est un SVG
    if img_path.lower().endswith(".svg"):
        convert_command_template = config['Settings']['IMConvertCommandSVG']
    else:
        convert_command_template = config['Settings']['IMConvertCommand']

    convert_command = convert_command_template.format(
        MarqueeBackgroundColor=config['Settings']['MarqueeBackgroundColor'],
        IMPath=config['Settings']['IMPath'],
        MarqueeWidth=marquee_width,
        MarqueeHeight=marquee_height,
        MarqueeWidthBorderLess=marquee_width - 2 * marquee_border,
        MarqueeHeightBorderLess=marquee_height - 2 * marquee_border,
        ImgPath=img_path,
        ImgTargetPath=target_img_path
    )

    subprocess.run(convert_command, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)
    return target_img_path

def find_file(base_path):
    for fmt in config['Settings']['AcceptedFormats'].split(','):
        full_path = f"{base_path}.{fmt.strip()}"
        full_path_topper = f"{base_path}-topper.png"
        full_path_scrapped = f"{base_path}scrapped.{fmt.strip()}"
        # Test fichier si marquee standard
        #logging.info(f"###FF TEST full_path : {full_path}")
        if os.path.isfile(full_path):
            if config['Settings']['MarqueeAutoConvert'] == "true":
                # Optimisation
                #logging.info(f"###FF File found : {full_path} >> Convert to marquee size PNG")
                return convert_image(full_path, full_path_topper)
            else:
                # Conservation du fichier d'origine
                logging.info(f"###FF File found : {full_path} >> Using the found file")
                return full_path
        # Test si topper optimisé déja existant
        logging.info(f"###FF TEST full_path_topper : {full_path_topper}")
        if os.path.isfile(full_path_topper):
            logging.info(f"###FF Full_path_topper Detected : {full_path_topper}")
            return full_path_topper
        # Test fichier si marquee scrappé
        #logging.info(f"###FF TEST full_path_scrapped : {full_path_scrapped}")
        if os.path.isfile(full_path_scrapped):
            #logging.info(f"###FF Scraped topper file found : {full_path_scrapped} >> Convert to marquee size PNG")
            return convert_image(full_path_scrapped, full_path_topper)

    # Aucun fichier trouvé >> test SVG
    full_path_suffixe=config['Settings']['MarqueeWhiteTextAlternativNameSuffix']
    full_path_convert_svg = f"{base_path}-topper.png"
    # Test si fond noir, recherche svg texte blanc
    if config['Settings']['MarqueeBackgroundColor'] == "Black":
        full_path_backgroundBoWcolor_svg = f"{base_path}{full_path_suffixe}.svg"
        #logging.info(f"###FF TEST : {full_path_backgroundBoWcolor_svg}")
        if os.path.isfile(full_path_backgroundBoWcolor_svg):
            #logging.info(f"###FF SVG File White Text found : {full_path_backgroundBoWcolor_svg}")
            return convert_image(full_path_backgroundBoWcolor_svg, full_path_convert_svg)
    full_path_svg = f"{base_path}.svg"
    # Test standard
    #logging.info(f"###FF TEST : {full_path_svg}")
    if os.path.isfile(full_path_svg):
        #logging.info(f"###FF SVG File found : {full_path_svg}")
        return convert_image(full_path_svg, full_path_convert_svg)
    logging.info(f"FF No File found : {base_path} - {full_path} - {full_path_svg} - {full_path_convert_svg}")
    return None

def add_to_scrap_pool(system_name, game_title, game_name, marquee_path, full_marquee_path, rom_path):
    scrap_pool_file = 'scrap.pool'
    # Vérifier si scrap.pool n'existe pas, le créer
    if not os.path.exists(scrap_pool_file):
        #logging.info(f"ATSP Create scrap.pool file")
        open(scrap_pool_file, 'w').close()

    # Ajouter la demande dans le fichier scrap.pool
    with open(scrap_pool_file, 'a') as file:
        file.write(f"{system_name}|{game_title}|{game_name}|{marquee_path}|{full_marquee_path}|{rom_path}\n")
        #logging.info(f"Add {system_name}, {game_title} ,{game_name} to scrap.pool file")

#action=game-selected&param1="amstradcpc"&param2="C:/RetroBatV6/roms/amstradcpc/007 - Live and Let Die (1988)(Domark).zip"&param3="Live and Let Die" // game
#action=system-selected&param1="amstradcpc" // systems
#action=system-selected&param1="all"  event=system-selected&param1="favorites" // collections
def parse_path(action, params, systems_config):
    system_name = ''
    game_name = ''
    game_title = ''
    rom_path = ''

    if action == 'game-selected' :
        system_name = params.get('param1', '')
        decoded_param = urllib.parse.unquote_plus(params.get('param1', ''))
        formatted_path = os.path.normpath(urllib.parse.unquote_plus(params.get('param2', '')))
        folder_rom_name = systems_config.get(decoded_param, '')
        folder_rom_path = os.path.join(config['Settings']['RomsPath'], folder_rom_name)
        game_title = params.get('param3', '')
        #  Recupération system si pas dans es_systems
        if folder_rom_name == '' :
            roms_path = config['Settings']['RomsPath']
            if formatted_path.startswith(roms_path):
                folder_rom_name = formatted_path[len(roms_path):].strip('\\/')
                folder_rom_name = folder_rom_name.split('\\')[0] if '\\' in folder_rom_name else folder_rom_name
                system_name = folder_rom_name
            else:
                folder_rom_name = os.path.basename(os.path.normpath(formatted_path))

        # Test si la chaine est simplement le nom du dossier contenant les roms (avec la table de correspondance folder_rom_name)
        if folder_rom_name and os.path.isdir(folder_rom_path):
            system_name = decoded_param

        # Recuperation game_name
        rom_path = formatted_path
        if os.path.isdir(formatted_path):
            game_name = os.path.splitext(os.path.basename(formatted_path))[0]
            logging.info(f"PP Path System Roms Folder: {system_name}, Game name : {game_name}, Game title : {game_title}")
            if system_name != game_name:
                return system_name, game_name, game_title, rom_path

        # Test si le chemin correspond bien à une rom d'un dossier système
        if os.path.isfile(formatted_path):
            path_parts = formatted_path.split(os.sep)
            game_name = os.path.splitext(os.path.basename(formatted_path))[0]
            if system_name == '' :
                system_name = path_parts[-2] if len(path_parts) > 1 else ''
            logging.info(f"PP Path File System rom folder: {system_name}, Game name : {game_name}, Game title : {game_title}")
            rom_path = formatted_path
            if system_name != game_name:
                return system_name, game_name, game_title, rom_path

        return system_name, game_name, game_title, rom_path

    elif action == 'system-selected' :
        folder_rom_name = params.get('param1', '')
        folder_rom_path = os.path.join(config['Settings']['RomsPath'], folder_rom_name)
        # Test si la chaine est simplement le nom du dossier contenant les roms (avec la table de correspondance folder_rom_name)
        if folder_rom_name and os.path.isdir(folder_rom_path):
            system_name = folder_rom_name
            return system_name, '', '', ''
        else:
            return 'collection', params.get('param1', ''), '', ''

    return '', '', '', ''

def execute_command(action, params, systems_config):
    global last_execution_time
    if action in config['Commands']:
        system_name, game_name, game_title, rom_path = parse_path(action, params, systems_config)
        logging.info(f"execute_command system_name {system_name}, game_name {game_name} ,game_title {game_title}, rom_path {rom_path}")
        marquee_file = find_marquee_file(system_name, game_name, systems_config, game_title, rom_path)
        #escaped_marquee_file = escape_file_path(marquee_file)
        command = config['Commands'][action].format(
            marquee_file=marquee_file,
            IPCChannel=config['Settings']['IPCChannel']
        )
        logging.info(f"Executing the command : {command}")
        subprocess.run(command, shell=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)
        last_execution_time = time.time()
        return json.dumps({"status": "success", "action": action, "command": command})
    return json.dumps({"status": "error", "message": "No command configured for this action"})

# EVENT RECEPTIONNE CLASSIQUE (PAR EXE ou PS1)
# Variable globale pour stocker le timestamp de la dernière requête
last_execution_time = 0  # Timestamp de la dernière exécution
request_list = []        # Liste pour stocker les requêtes
import time
import threading
def monitor_and_execute_requests():
    global request_list
    while True:
        current_time = time.time()
        with lock:
            # S'assurer que la liste est triée correctement par timestamp en ordre croissant
            request_list.sort(key=lambda x: x[0])

            if request_list and current_time - last_execution_time >= 1:
                # Exécuter la commande pour la requête avec le plus grand timestamp
                latest_request = max(request_list, key=lambda x: x[0])
                _, action, params = latest_request
                execute_command(action, params, systems_config)

                # Conserver uniquement les requêtes arrivées après l'exécution de la commande
                latest_timestamp = latest_request[0]
                request_list = [req for req in request_list if req[0] > latest_timestamp]
        time.sleep(0.2)

@app.route('/', methods=['GET'])
def handle_request():
    global request_list
    ensure_mpv_running()
    action = request.args.get('event', '')
    params = dict(request.args)
    params.pop('event', None)
    logging.info(f"Action received : {action}, Parameters : {params} -+")

    if 'timestamp' in params:
        with lock:
            request_list.append((float(params['timestamp']), action, params))

    return "Request received"

#@app.route('/', methods=['GET'])
#def handle_request():
    #subprocess.run(f"taskkill /IM ESEventPush.exe /F", shell=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
#    ensure_mpv_running()
#    action = request.args.get('event', '')
#    params = dict(request.args)
#    logging.info(f"Action received : {action}, Parameters : {params}")
#    params.pop('event', None)
#    return execute_command(action, params, systems_config)

# EVENT SURVEILLE PAR LECTURE FICHIER ARG
from watchdog.observers import Observer
from watchdog.events import FileSystemEventHandler


class FileWatcher(FileSystemEventHandler):
    def __init__(self, file_path, callback):
        self.file_path = file_path
        self.callback = callback

    def on_modified(self, event):
        if event.src_path == self.file_path:
            self.callback()

def on_file_modified():
    ensure_mpv_running()
    file_path = os.path.join(config['Settings']['RetroBatPath'], 'plugins', 'MarqueeManager', 'ESEvent.arg')
    logging.info(f"on_file_modified : {file_path}")
    # Lire et analyser le contenu du fichier
    try:
        with open(file_path, 'r') as file:
            content = file.read().strip()
        content = content.replace('|', '!')
        logging.info(f"on_file_modified content : {content}")
        params = urllib.parse.parse_qs(content)
        action = params.get('event', [''])[0]  # Prend le premier élément ou une chaîne vide

        # Nettoyer les paramètres
        params.pop('event', None)
        for key, value in params.items():
            params[key] = value[0].strip(' "')

        logging.info(f"Action received : {action}, Parameters : {params} --")

        # Ici, appeler votre fonction execute_command
        return execute_command(action, params, systems_config)

    except Exception as e:
        logging.error(f"Error processing file: {e}")

def start_watching():
    logging.info(f"start_watching")
    file_path = os.path.join(config['Settings']['RetroBatPath'], 'plugins', 'MarqueeManager', 'ESEvent.arg')
    observer = Observer()
    event_handler = FileWatcher(file_path, on_file_modified)
    logging.info(f"file_path {file_path}")
    observer.schedule(event_handler, os.path.dirname(file_path), recursive=False)
    observer.start()
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        observer.stop()
    observer.join()

if __name__ == '__main__':
    load_config()
    lock = threading.Lock()

    # Démarrer la surveillance du fichier
    file_thread = threading.Thread(target=start_watching)
    file_thread.start()
    #logging.info(f"File watching thread started: {file_thread.is_alive()}")

    # Démarrer le thread de surveillance de requete http
    #monitor_thread = threading.Thread(target=monitor_and_execute_requests, daemon=True)
    #monitor_thread.start()

    launch_media_player()
    systems_config = load_systems_config(os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'es_systems.cfg'))
    app.run(host=config['Settings']['Host'], port=int(config['Settings']['Port']), debug=False)

