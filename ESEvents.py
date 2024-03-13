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

    # Tester les chemins
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

def find_system_marquee(system_name, folder_rom_name, systems_config):
    marquee_structure = config['Settings']['SystemFilePath']
    marquee_paths = [
        marquee_structure.format(system_name=folder_rom_name),
        marquee_structure.format(system_name=systems_config.get(system_name + ".theme"))
    ]

    # Tester les chemins
    for marquee_path in marquee_paths:
        if not system_name and not folder_rom_name and not marquee_path:
            marquee_path = 'retrobat'
        full_marquee_path = os.path.join(config['Settings']['SystemMarqueePath'], marquee_path)
        logging.info(f"FMF System topper path (full_marquee_path) : {full_marquee_path}")

        marquee_file = find_file(full_marquee_path)
        if marquee_file:
            logging.info(f"FMF System Topper : {marquee_file}")
            return marquee_file

    return None


#        return 'collection', collection, system_folder, system_essystems
#        return 'system', system_name, system_folder, system_essystems
#        return 'game', system_name, game_name, game_title, rom_path

def find_marquee_file(type, param1, param2, param3, param4, systems_config):
    logging.info(f"##################################################")
    logging.info(f"################ NEW MARQUEE #####################")
    logging.info(f"FMF find_marquee_file : type : {type} - param1 : {param1} - param2 : {param2} - param3 : {param3} - param4 : {param4}")
    lng = config['Settings']['Language']
    marquee_structure = config['Settings']['MarqueeFilePath']
    marquee_structure_default = config['Settings']['MarqueeFilePathDefault']
    #rom_path = os.path.normpath(urllib.parse.unquote_plus(param3, ''))) #C:\RetroBat\roms\<system>\<rom.ext>
    if type == 'collection':
        marquee_file = find_marquee_for_collection(param1)
        if marquee_file:
            logging.info(f"FMF Found collection : {marquee_file}")
            return marquee_file

    if type == 'system':
        folder_rom_name = systems_config.get(param1, param1)
        marquee_file = find_system_marquee(param1, folder_rom_name, systems_config)
        if marquee_file:
            #system_name = {param1}
            logging.info(f"FMF Found system : {marquee_file}")
            return marquee_file

    if type == 'game':
        system_name = param1
        game_name = param2
        game_title = param3
        rom_path = param4
        folder_rom_name = systems_config.get(param1, param1)

        logging.info(f"FMF GAME marquee_structure : {marquee_structure} system_name : {system_name} - game_name : {game_name} - folder_rom_name : {folder_rom_name} - rom_path : {rom_path}")

        # Priorité sur le pattern name basic
        marquee_path = marquee_structure.format(system_name=folder_rom_name, game_name=game_name)
        full_marquee_path = os.path.join(config['Settings']['MarqueeImagePath'], marquee_path)
        marquee_file = find_file(full_marquee_path)
        logging.info(f"FMF Full_marquee_path : {full_marquee_path} > marquee_file : {marquee_file}")

        # Pattern default
        if marquee_file is None:
            logging.info(f"FMF marquee_file is None")
            marquee_path_default = marquee_structure_default.format(system_name=folder_rom_name, game_name=game_name)
            full_marquee_path_default = os.path.join(config['Settings']['MarqueeImagePathDefault'], marquee_path_default)
            marquee_file = find_file(full_marquee_path_default)
            logging.info(f"FMF full_marquee_path_default : {full_marquee_path_default}")

        # Lancer le scraping si MarqueeAutoScraping est activé et si le marquee est trouvé avec le chemin par défaut
        if marquee_file and game_title is not None and game_title != '' and config['Settings']['MarqueeAutoScraping'] == "true":
            logging.info(f"FMF Scraping active : {full_marquee_path}")
            add_to_scrap_pool(system_name, game_title, game_name, marquee_path, full_marquee_path, rom_path)

        if marquee_file:
            logging.info(f"FMF return marquee_file")
            #logging.info(f"FMF Found game topper : {marquee_file}")
            return marquee_file

    logging.info(f"FMF Using the default image : {config['Settings']['DefaultImagePath']}")
    return config['Settings']['DefaultImagePath']

def find_file(base_path):
    logging.info(f"##################################################")
    logging.info(f"###FF FIND FILE TEST : {base_path}")
    for fmt in config['Settings']['AcceptedFormats'].split(','):
        logging.info(f"###FF FORMAT TESTED : {fmt.strip()}")
        full_path = f"{base_path}.{fmt.strip()}"
        full_path_topper = f"{base_path}-topper.png"
        full_path_scrapped = f"{base_path}scrapped.{fmt.strip()}"
        # Test fichier si marquee standard
        logging.info(f"###FF TEST full_path : {full_path}")
        if os.path.isfile(full_path):
            if config['Settings']['MarqueeAutoConvert'] == "true":
                # Optimisation
                logging.info(f"###FF File found : {full_path} >> Convert to marquee size PNG")
                return convert_image(full_path, full_path_topper)
            else:
                # Conservation du fichier d'origine
                logging.info(f"###FF File found : {full_path} >> Using the found file")
                return full_path
        else:
            logging.info(f"###FF full_path NOT Detected")
        # Test si topper optimisé déja existant
        logging.info(f"###FF TEST full_path_topper : {full_path_topper}")
        if os.path.isfile(full_path_topper):
            logging.info(f"###FF Full_path_topper Detected : {full_path_topper}")
            return full_path_topper
        else:
            logging.info(f"###FF Full_path_topper NOT Detected")
        # Test fichier si marquee scrappé
        logging.info(f"###FF TEST full_path_scrapped : {full_path_scrapped}")
        if os.path.isfile(full_path_scrapped):
            #logging.info(f"###FF Scraped topper file found : {full_path_scrapped} >> Convert to marquee size PNG")
            return convert_image(full_path_scrapped, full_path_topper)
        else:
            logging.info(f"###FF full_path_scrapped NOT Detected")
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

#action=game-start&param1="C:\RetroBatV6\roms\amstradcpc\Back To The Future II (UK) (1990) (Trainer).zip"&param2="Back To The Future II (UK) (1990) (Trainer)"&param3="Back to the Future Part II"
#action=game-selected&param1="amstradcpc"&param2="C:/RetroBatV6/roms/amstradcpc/007 - Live and Let Die (1988)(Domark).zip"&param3="Live and Let Die" // game
#action=system-selected&param1="amstradcpc" // systems
#action=system-selected&param1="all"  event=system-selected&param1="favorites" // collections
def parse_path(action, params, systems_config):
    system_name = ''
    system_essystems = ''
    system_folder = False
    system_essystems = False
    game_name = ''
    game_title = ''
    folder_rom_name = ''
    folder_rom_path = ''
    roms_path = config['Settings']['RomsPath'] # C:\RetroBat\roms
    folder_rom_name_extract = ''
    system_rom_name_extract = ''

    if action == 'game-selected' or action == 'system-selected' :
        system_name = params.get('param1', '')
        system_rom_path = os.path.join(roms_path, params.get('param1', '')) # C:\RetroBat\roms\<system>
        #folder_rom_name = urllib.parse.unquote_plus(params.get('param1', ''))
        formatted_rom_path = os.path.normpath(urllib.parse.unquote_plus(params.get('param2', ''))) #C:\RetroBat\roms\<system>\<rom.ext>
        # Extraction du chemin de la rom , du dossier system du jeu et du jeu
        folder_rom_name_extract = folder_rom_path[len(formatted_rom_path):].strip('\\/')
        #system_rom_name_extract = folder_rom_name.split('\\')[0] if '\\' in formatted_rom_path
        logging.info(f"PP folder_rom_name_extract - {folder_rom_name_extract}")
        #logging.info(f"PP system_rom_name_extract - {system_rom_name_extract}")

        # Test si la chaine system est dans le fichier es_systems.cfg
        if systems_config.get(system_name, ''):
            system_essystems = True
            logging.info(f"PP system_essystems - {system_essystems}")
        # Test si la chaine system est un dossier system existant qui suit /roms/
        elif os.path.isdir(system_rom_path):
            system_folder = True
            logging.info(f"PP system_folder - {system_folder}")
        # Test si le chemin de la rom est un dossier
        elif os.path.isdir(formatted_rom_path):
            folder_rom_name = os.path.basename(os.path.normpath(formatted_rom_path))
            logging.info(f"PP game - le chemin de la rom est un dossier")
        # Test si le chemin de la rom est un fichier
        elif os.path.isfile(formatted_rom_path):
            path_parts = formatted_rom_path.split(os.sep)
            game_name = os.path.splitext(os.path.basename(formatted_rom_path))[0]
            system_name = path_parts[-2] if len(path_parts) > 1 else ''
            logging.info(f"PP game - le chemin de la rom est un fichier")

    # GAME START
    if action == 'game-start' :
        game_name = params.get('param2', '')
        game_title = params.get('param3', '')
        formatted_rom_path = os.path.normpath(urllib.parse.unquote_plus(params.get('param1', '')))
        logging.info(f"PP GAME START formatted_rom_path - {formatted_rom_path}")
        remaining_path = formatted_rom_path.replace(roms_path, "")
        if remaining_path.startswith("\\"):
            remaining_path = remaining_path[1:]
        system_name = remaining_path.split('\\')[0]
        #logging.info(f"PP GAME-START remaining_path : {remaining_path}, system_name : {system_name}")
        logging.info(f"PP GAME-START system_name : {system_name}, game name : {game_name}, game title : {game_title}, rom_path : {formatted_rom_path}")
        return 'game', system_name, game_name, game_title, formatted_rom_path

    # GAME SELECTED
    if action == 'game-selected' :
        game_title = params.get('param3', '')
        formatted_rom_path = os.path.normpath(urllib.parse.unquote_plus(params.get('param2', '')))
        logging.info(f"PP GAME SELECTED formatted_rom_path : {formatted_rom_path}")
        #if os.path.isdir(formatted_rom_path):
        #    game_name = os.path.splitext(os.path.basename(formatted_path))[0]

        # Si fichier,
        if os.path.isfile(formatted_rom_path):
            # Extrait le nom de base du fichier sans extension
            game_name = os.path.splitext(os.path.basename(formatted_rom_path))[0]
        # Si dossier,
        elif os.path.isdir(formatted_rom_path):
            # Extrait le nom du dossier
            game_name = os.path.basename(formatted_rom_path)

        logging.info(f"PP GAME-SELECTED system_name : {system_name}, game name : {game_name}, game title : {game_title}, rom_path : {formatted_rom_path}")
        return 'game', system_name, game_name, game_title, formatted_rom_path

        # ICI J AI RAJOUTE UN PARAM GAME EN DESSOUS DYSTEM
        # Test si le chemin correspond bien à une rom d'un dossier dans système
        #if os.path.isfile(formatted_path):
        #    path_parts = formatted_path.split(os.sep)
        #    game_name = os.path.splitext(os.path.basename(formatted_path))[0]
        #    if system_name == '' :
        #        system_name = path_parts[-2] if len(path_parts) > 1 else ''
        #    logging.info(f"PP Path File System rom folder: {system_name}, Game name : {game_name}, Game title : {game_title}")
        #    rom_path = formatted_path
        #    if system_name != game_name:
        #        return 'system', system_name, '', ''
        #
        #return 'system', system_name, '', ''

    # SYSTEM / COLLECTION
    elif action == 'system-selected' :
        if system_folder == True and system_essystems == True:
            system_name = params.get('param1', '')
            logging.info(f"PP system : {system_name}, system_folder : {system_folder}, system_essystems {system_essystems}")
            return 'system', system_name, system_folder, system_essystems, ''
        else:
            collection = params.get('param1', '')
            logging.info(f"PP collection : {system_name}, system_folder : {system_folder}, system_essystems {system_essystems}")
            return 'collection', collection, system_folder, system_essystems, ''

    return '', '', '', '', ''

def execute_command(action, params, systems_config):
    global last_execution_time
    if action in config['Commands']:
        type, param1, param2, param3, param4 = parse_path(action, params, systems_config)
        logging.info(f"execute_command type {type}, param1 {param1} ,param2 {param2}, param3 {param3} ,param4 {param4}")
        marquee_file = find_marquee_file(type, param1, param2, param3, param4, systems_config)
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
    systems_config = load_systems_config(os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'es_systems.cfg'))
    lock = threading.Lock()

    # Démarrer la surveillance du fichier
    file_thread = threading.Thread(target=start_watching)
    file_thread.start()
    logging.info(f"File watching thread started: {file_thread.is_alive()}")

    # Démarrer le thread de surveillance de requete http
    #monitor_thread = threading.Thread(target=monitor_and_execute_requests, daemon=True)
    #monitor_thread.start()

    launch_media_player()
    app.run(host=config['Settings']['Host'], port=int(config['Settings']['Port']), debug=False)

