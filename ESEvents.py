#from flask import Flask, request
import configparser
import subprocess
import os
import json
import urllib.parse
import shlex
import xml.etree.ElementTree as ET
import glob
import logging
import re
import threading
import time

creation_flags = 0
if sys.platform == "win32":  # Uniquement pour Windows
    creation_flags = subprocess.CREATE_NO_WINDOW

# Variables globales pour éviter des vérifications simultanées de dmd.exe
dmd_check_lock = threading.Lock()
dmd_check_in_progress = False
game_start_occurred = False

def check_and_launch_dmd():
    global dmd_check_in_progress
    with dmd_check_lock:
        if dmd_check_in_progress:
            # Une vérification est déjà en cours, on annule ce nouvel événement
            return
        dmd_check_in_progress = True
    try:
        if not is_dmd_running():
            launch_process("dmd/dmd.exe")
    finally:
        with dmd_check_lock:
            dmd_check_in_progress = False

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
    update_path('DefaultFanartPath', os.path.join(current_working_dir, 'images', 'defaultfanart.png'))
    update_path('MarqueeImagePath', os.path.join(current_working_dir, 'images'))
    update_path('MarqueeImagePathDefault', os.path.join(config['Settings']['RetroBatPath'], 'roms'))
    update_path('FanartMarqueePath', os.path.join(config['Settings']['RetroBatPath'], 'roms'))
    update_path('SystemMarqueePath', os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'themes', 'es-theme-carbon-master', 'art', 'logos'))
    update_path('CollectionMarqueePath', os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'themes', 'es-theme-carbon-master', 'art', 'logos'))
    update_path('MPVPath', os.path.join(current_working_dir, 'mpv', 'mpv.exe'))
    update_path('IMPath', os.path.join(current_working_dir, 'imagemagick', 'convert.exe'))


systems_config = None
def load_systems_config(xml_relative_path):
    script_dir = os.path.dirname(os.path.realpath(__file__))
    xml_path = os.path.join(script_dir, xml_relative_path)
    tree = ET.parse(xml_path)
    root = tree.getroot()

    system_folders = {}
    logging.info(f"##### load_systems_config {xml_relative_path}")
    for system in root.findall('system'):
        name_elem = system.find('name')
        path_elem = system.find('path')
        theme_elem = system.find('theme')

        if name_elem is not None and path_elem is not None:
            name = name_elem.text
            path = path_elem.text if path_elem is not None else "default_path"
            if path is None or path.strip() == "":
                    path = "default_path"
            theme = theme_elem.text if theme_elem is not None else "default_theme"

            # Recherche du nom du dossier des roms
            roms_path = config['Settings']['RomsPath']
            folder_rom_name = os.path.basename(os.path.normpath(path.strip('~\\..')))
            system_folders[name] = folder_rom_name
            system_folders[name + ".path"] = path
            system_folders[name + ".theme"] = theme
            logging.info(f"System {name} loading folder_rom_name - {folder_rom_name} path {path} - theme : {system_folders[name + '.theme']}")

        else:
            logging.info(f"Missing name and/or path in {xml_relative_path} for a system {name_elem} {path_elem}")

    return system_folders

def load_all_systems_configs(config_directory):
    all_system_folders = {}

    # Liste tous les fichiers es_systems*.cfg
    cfg_files = glob.glob(os.path.join(config_directory, 'es_systems*.cfg'))
    for cfg_file in cfg_files:
        system_folders = load_systems_config(cfg_file)
        all_system_folders.update(system_folders)

    return all_system_folders

def launch_media_player():
    kill_command = config['Settings']['MPVKillCommand']
    subprocess.run(kill_command, shell=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)
    logging.info(f"Execute kill command : {kill_command}")

    # Création de la commande de lancement de mpv
    launch_command = config['Settings']['MPVLaunchCommand'].format(
        MPVPath=config['Settings']['MPVPath'],
        IPCChannel=config['Settings']['IPCChannel'],
        ScreenNumber=config['Settings'].get('ScreenNumber', '1'),
        DefaultImagePath=config['Settings']['DefaultImagePath'],
        MarqueeBackgroundCodeColor=config['Settings']['MarqueeBackgroundCodeColor']
    )

    # Si ActiveDMD est à true, ajouter le flag --window-minimized=yes
    if config['Settings']['ActiveDMD'] == 'true':
        launch_command += " --window-minimized=yes"

    # Lancement de mpv
    subprocess.Popen(launch_command, shell=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=creation_flags)
    logging.info(f"MPV launch command executed : {launch_command}")

def launch_process(process):
    logging.info(f"Execute process : {process}")
    subprocess.Popen(f"\"{process}\"", shell=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=creation_flags)
    logging.info(f"Launch process : {process}")

def is_mpv_running():
    try:
        test_command = config['Settings']['MPVTestCommand'].format(IPCChannel=config['Settings']['IPCChannel'])
        subprocess.run(test_command, shell=True, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)
        logging.info(f"MPV is currently running.")
        return True
    except subprocess.CalledProcessError as e:
        # Log de l'erreur en cas d'échec de la commande
        logging.info(f"MPV is not currently running. Error: {e}")
        #logging.info(f"Command error output: {e.stderr.decode().strip()}")
        return False

def is_dmd_running():
    try:
        # Utilise la commande tasklist sous Windows pour vérifier si dmd.exe est actif
        result = subprocess.run('tasklist /FI "IMAGENAME eq dmd.exe"', shell=True, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)
        output = result.stdout.decode('cp850')
        if "dmd.exe" in output:
            logging.info("dmd.exe est déjà actif.")
            return True
    except subprocess.CalledProcessError as e:
        logging.info(f"dmd.exe non actif. Erreur: {e}")
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
    # Création du dossier parent s'il n'existe pas
    parent_dir = os.path.dirname(target_img_path)
    if not os.path.exists(parent_dir):
        os.makedirs(parent_dir)

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
    logging.info(f"&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&&")
    logging.info(f"FMF find_marquee_for_collection")
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

full_marquee_path = None
def find_system_marquee(system_name, folder_rom_name, systems_config):
    logging.info(f"@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@")
    logging.info(f"FMF find_system_marquee")
    marquee_structure = config['Settings']['SystemFilePath']
    marquee_paths = [
        marquee_structure.format(system_name=folder_rom_name),
        marquee_structure.format(system_name=systems_config.get(system_name + ".theme"))
    ]
    logging.info(f"FMF marquee_paths : {marquee_paths}")
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

current_system_name = None
current_game_name = None
current_game_title = None
current_rom_path = None
def find_marquee_file(type, param1, param2, param3, param4, systems_config):
    global current_system_name, current_game_name, current_game_title, current_rom_path

    logging.info(f"##################################################")
    logging.info(f"################ NEW MARQUEE #####################")
    logging.info(f"FMF find_marquee_file : type : {type} - param1 : {param1} - param2 : {param2} - param3 : {param3} - param4 : {param4}")
    lng = config['Settings']['Language']
    marquee_structure = config['Settings']['MarqueeFilePath']
    marquee_structure_default = config['Settings']['MarqueeFilePathDefault']
    marquee_file = None
    #rom_path = os.path.normpath(urllib.parse.unquote(param3, ''))) #C:\RetroBat\roms\<system>\<rom.ext>
    if type == 'collection':
        logging.info(f"############# COLLECTION ###############")
        marquee_file = find_marquee_for_collection(param1)
        if not marquee_file:
            folder_rom_name = systems_config.get(param1, param1)
            marquee_file = find_system_marquee(param1, folder_rom_name, systems_config)
        if marquee_file:
            logging.info(f"FMF Found collection : {marquee_file}")
            return marquee_file

    elif type == 'system':
        logging.info(f"############# SYSTEM ###############")
        folder_rom_name = systems_config.get(param1, param1)
        marquee_file = find_system_marquee(param1, folder_rom_name, systems_config)
        if marquee_file:
            #system_name = {param1}
            logging.info(f"FMF Found system : {marquee_file}")
            return marquee_file

    elif type == 'game' or type == 'game-forceupdate':
        logging.info(f"############# GAME ###############")
        current_system_name = system_name = param1
        current_game_name = game_name = param2
        current_game_title = game_title = param3
        current_rom_path = rom_path = param4
        folder_rom_name = systems_config.get(param1, param1)

        logging.info(f"FMF GAME marquee_structure : {marquee_structure} system_name : {system_name} - game_name : {game_name} - folder_rom_name : {folder_rom_name} - rom_path : {rom_path}")

        marquee_path = marquee_structure.format(system_name=folder_rom_name, game_name=game_name)
        full_marquee_path = None  # Initialisation

        # Pattern topper
        marquee_path_topper = f"{marquee_path}-topper"
        full_marquee_path_topper = os.path.join(config['Settings']['MarqueeImagePath'], marquee_path_topper)
        logging.info(f"############# SUB GAME PATTERN TOPPER ###############")
        marquee_file = find_file(full_marquee_path_topper)
        logging.info(f"FMF TOPPER Full_marquee_path : {full_marquee_path_topper} > marquee_file : {marquee_file}")

        # Si le fichier est trouvé avec le pattern topper, on affecte full_marquee_path
        if marquee_file is not None:
            full_marquee_path = full_marquee_path_topper

        # Recherche avec le pattern basic si nécessaire
        if marquee_file is None and type != 'game-forceupdate':
            full_marquee_path = os.path.join(config['Settings']['MarqueeImagePath'], marquee_path)
            logging.info(f"############# SUB GAME PATTERN BASIC ###############")
            marquee_file = find_file(full_marquee_path)
            logging.info(f"FMF BASIC Full_marquee_path : {full_marquee_path} > marquee_file : {marquee_file}")

        if type == 'game-forceupdate' and os.path.exists(marquee_file) and "-topper" in marquee_file:
            logging.info(f"FMF REMOVE {marquee_file}")
            os.remove(marquee_file)
            marquee_file = None

        # Lancer la génération automatique du marquee si on dispose d'un fanart et d'un logo et si MarqueeAutoGeneration est à true
        if marquee_file is None and config['Settings']['MarqueeAutoGeneration'] == "true":
            logging.info(f"FMF MarqueeAutoGeneration active")
            marquee_path = marquee_structure.format(system_name=folder_rom_name, game_name=game_name)
            compose_marquee_path = os.path.join(config['Settings']['MarqueeImagePath'], marquee_path)
            full_compose_marquee_path_topper = f"{compose_marquee_path}-topper.png"
            marquee_file = autogen_marquee(system_name, game_name, rom_path, full_compose_marquee_path_topper)

        # Pattern default
        if marquee_file is None:
            logging.info(f"############# SUB GAME NONE ###############")
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

        # Affichage system si aucun marquee trouvé pour le jeu
        folder_rom_name = systems_config.get(param1, param1)
        marquee_file = find_system_marquee(param1, folder_rom_name, systems_config)
        if marquee_file:
            logging.info(f"FMF Found system : {marquee_file}")
            return marquee_file


    logging.info(f"FMF Using the default image : {config['Settings']['DefaultImagePath']}")
    return config['Settings']['DefaultImagePath']

def find_fanart_file(file_type, param1, param2, param3, param4, systems_config):
    """
    Renvoie le chemin du fanart en fonction du type.

    Pour un jeu (file_type 'game' ou 'game-forceupdate'):
      - param1 : system_name
      - param2 : game_name
      - param3 : game_title
      - param4 : rom_path

    Pour un système (file_type 'system'):
      - param1 : system_name

    systems_config est un dictionnaire pour récupérer le nom de dossier du système.
    """
    global current_system_name, current_game_name, current_game_title, current_rom_path

    logging.info(f"#####>> find_fanart_file : type: {file_type} - param1: {param1} - param2: {param2} - param3: {param3} - param4: {param4}")

    roms_path = config['Settings']['RomsPath']  # par exemple "C:\\RetroBat\\roms"
    fanart_file = None

    if file_type == 'system':
        # Pour un système, le chemin de fanart se base sur FanartSystemFilePath
        folder_rom_name = systems_config.get(param1, param1)
        # Exemple : FanartSystemFilePath = {system_name}
        fanart_relative = config['Settings'].get('FanartSystemFilePath', "{system_name}").format(system_name=folder_rom_name)
        full_fanart_path = os.path.join(roms_path, fanart_relative + ".jpg")
        logging.info(f"find_fanart_file (system) full path: {full_fanart_path}")
        if os.path.exists(full_fanart_path):
            return full_fanart_path

    elif file_type in ['game', 'game-forceupdate']:
        # Pour un jeu, on extrait les informations
        current_system_name = param1
        current_game_name = param2
        current_game_title = param3
        current_rom_path = param4
        folder_rom_name = systems_config.get(param1, param1)

        # Construction du chemin par défaut pour le fanart
        base_image_path = os.path.join(roms_path, param1, "images")
        fanart_file_name = f"{param2}-fanart.jpg"
        full_fanart_path = os.path.join(base_image_path, fanart_file_name).replace("\\", "\\\\")
        logging.info(f"find_fanart_file DEFAULT - fanart_file_path: {full_fanart_path}")

        # Si le fichier n'existe pas, on utilise la structure configurée
        if not os.path.exists(full_fanart_path):
            fanart_structure = config['Settings'].get('FanartGameFilePath')
            fanart_path = fanart_structure.format(system_name=folder_rom_name, game_name=param2)
            full_fanart_path = os.path.join(roms_path, param1, fanart_path.strip('.\\'))
            logging.info(f"find_fanart_file CONFIGINI - fanart_file_path: {full_fanart_path}")

        # Si le fichier n'existe toujours pas, tenter de récupérer le chemin depuis la gamelist
        if not os.path.exists(full_fanart_path):
            logging.info(f"find_fanart_file: {full_fanart_path} n'existe pas, recherche dans la gamelist")
            game_info = find_game_info_in_gamelist(param2, param1, roms_path)
            if game_info:
                fanart_rel_path = game_info.get('fanart', '').replace('/', '\\')
                if fanart_rel_path:
                    full_fanart_path = os.path.join(roms_path, param1, fanart_rel_path.strip('.\\'))
            logging.info(f"find_fanart_file GAMELIST - fanart_file_path: {full_fanart_path}")

        fanart_file = full_fanart_path

    if not fanart_file:
        logging.info(f"find_fanart_file: Using default fanart: {config['Settings']['DefaultFanartPath']}")
        fanart_file = config['Settings']['DefaultFanartPath']

    return fanart_file

def clean_rom_name(rom_name):
    # Supprime tout texte entre parenthèses ou crochets à la fin de la chaîne
    cleaned_name = re.sub(r'\s*(\[[^\]]*\]|\([^\)]*\))\s*$', '', rom_name)
    # Supprime les occurrences spécifiques de motifs comme "??-in-1"
    cleaned_name = re.sub(r'\s*\d+-in-\d+', '', cleaned_name)
    return cleaned_name

def find_file(base_path):
    logging.info(f"#########################################>>>>>")
    logging.info(f"#####>>>> FF FIND FILE TEST : {base_path}")
    logging.info(f"#########################################>>>>>")

    for fmt in config['Settings']['AcceptedFormats'].split(','):
        logging.info(f"###FF FORMAT TESTED : {fmt.strip()}")
        full_path = f"{base_path}.{fmt.strip()}"
        full_clean_path = f"{clean_rom_name(base_path)}.{fmt.strip()}"

        # Test fichier si marquee standard sans specification lng / code...
        logging.info(f"###FF TEST full_clean_path : {full_clean_path}")
        if os.path.isfile(full_clean_path):
            if config['Settings']['MarqueeAutoConvert'] == "true" and '-topper' not in base_path:
                # Optimisation
                full_path_topper = f"{base_path}-topper.png"
                logging.info(f"###FF File found : {full_path} >> Convert to marquee size PNG")
                return convert_image(full_clean_path, full_path_topper)
            else:
                # Conservation du fichier d'origine
                logging.info(f"###FF Clean file found : {full_clean_path} >> Using the found file")
                return full_clean_path

        # Test fichier si marquee standard
        logging.info(f"###FF TEST full_path : {full_path}")
        if os.path.isfile(full_path):
            if config['Settings']['MarqueeAutoConvert'] == "true" and '-topper' not in base_path:
                # Optimisation
                full_path_topper = f"{base_path}-topper.png"
                logging.info(f"###FF File found : {full_path} >> Convert to marquee size PNG")
                return convert_image(full_path, full_path_topper)
            else:
                # Conservation du fichier d'origine
                logging.info(f"###FF File found : {full_path} >> Using the found file")
                return full_path
        else:
            logging.info(f"###FF full_path NOT Detected")

        # Test fichier si marquee scrappé
        full_path_scrapped = f"{base_path}scrapped.{fmt.strip()}"
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
        logging.info(f"##SVG##FF TEST : {full_path_backgroundBoWcolor_svg}")
        if os.path.isfile(full_path_backgroundBoWcolor_svg):
            logging.info(f"##SVG##FF SVG File White Text found : {full_path_backgroundBoWcolor_svg}")
            return convert_image(full_path_backgroundBoWcolor_svg, full_path_convert_svg)
    full_path_svg = f"{base_path}.svg"
    # Test standard
    logging.info(f"##SVG##FF TEST : {full_path_svg}")
    if os.path.isfile(full_path_svg):
        logging.info(f"##SVG##FF SVG File found : {full_path_svg}")
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

from PIL import Image
import numpy as np

def analyze_image(image_path):
    global current_band
    marquee_width = int(config['Settings']['MarqueeWidth'])
    marquee_height = int(config['Settings']['MarqueeHeight'])

    img = Image.open(image_path).convert('L')  # Convertir en niveaux de gris
    img_np = np.array(img)

    # Ajuster l'image en fonction des paramètres du marquee
    cropped_img_np = img_np[(img_np.shape[0] - marquee_height) // 2:(img_np.shape[0] + marquee_height) // 2, :]

    # Sélectionner la bande centrale
    band_height = marquee_height // 3
    best_band = cropped_img_np[(current_band - 1) * band_height:current_band * band_height, :]
    #best_band = cropped_img_np[band_height:2 * band_height, :]

    # Fonction pour calculer la fréquence des changements de couleur
    def color_change_frequency(region):
        diff = np.abs(np.diff(region, axis=1))  # Différence horizontale
        return np.sum(diff)

    # Calculer la fréquence des changements de couleur pour chaque région
    quarter_width = marquee_width // 4
    half_width = marquee_width // 2
    freq_left = color_change_frequency(best_band[:, :quarter_width])
    freq_center = color_change_frequency(best_band[:, quarter_width:quarter_width + half_width])
    freq_right = color_change_frequency(best_band[:, -quarter_width:])

    # Déterminer la région avec la plus haute fréquence de changement de couleur
    horizontal_region = "left" if freq_left < min(freq_center, freq_right) else "center" if freq_center < freq_right else "right"

    if current_band == 1:
        vertical_region = "top"
    elif current_band == 2:
        vertical_region = "middle"
    else:  # current_band == 3
        vertical_region = "bottom"

    return horizontal_region, vertical_region

def find_game_info_in_gamelist(game_name, system_name, roms_path):
    gamelist_path = os.path.join(roms_path, system_name, "gamelist.xml")
    if not os.path.exists(gamelist_path):
        gamelist_path = os.path.join(systems_config.get(system_name + ".path"), "gamelist.xml")
        if not os.path.exists(gamelist_path):
            logging.info("Gamelist file not found.")
            return None

    tree = ET.parse(gamelist_path)
    root = tree.getroot()

    for game in root.findall('game'):
        game_path = game.find('path').text if game.find('path') is not None else None
        if game_path:
            extracted_game_name = os.path.splitext(os.path.basename(game_path))[0]
            if extracted_game_name == game_name:
                game_info = {child.tag: child.text for child in game}
                return game_info

    return None

def autogen_marquee(system_name, game_name, rom_path, target_img_path):
    global current_logo_align, current_logo_zoom, current_gradient_mode
    logging.info(f"#####>> autogen_marquee : system_name {system_name}, game_name {game_name}, rom_path {rom_path}, marquee_path {target_img_path}")

    # Création du dossier parent s'il n'existe pas
    parent_dir = os.path.dirname(target_img_path)
    if not os.path.exists(parent_dir):
        os.makedirs(parent_dir)

    # Chemin de base pour les images
    base_image_path = os.path.join(config['Settings']['RomsPath'], system_name, "images")
    roms_path = config['Settings']['RomsPath']  # Exemple : C:\RetroBat\roms

    # Construire les chemins de fichier pour le logo et le fanart
    logo_file_name = f"{game_name}-marquee.png"
    fanart_file_name = f"{game_name}-fanart.jpg"

    logo_file_path = os.path.join(base_image_path, logo_file_name).replace("\\", "\\\\")
    fanart_file_path = os.path.join(base_image_path, fanart_file_name).replace("\\", "\\\\")
    logging.info(f"autogen_marquee DEFAULT FOLDERS - logo_file_path {logo_file_path} fanart_file_path {fanart_file_path}")

    # Test du chemin personnalisé en priorité FANART
    if not os.path.exists(logo_file_path) or not os.path.exists(fanart_file_path):
        fanart_structure = config['Settings']['FanartGameFilePath']
        fanart_path = fanart_structure.format(system_name=system_name, game_name=game_name)

        marquee_structure = config['Settings']['MarqueeFilePath']
        marquee_path = marquee_structure.format(system_name=system_name, game_name=game_name)

        logo_file_path = os.path.join(roms_path, system_name, marquee_path.strip('.\\'))
        fanart_file_path = os.path.join(roms_path, system_name, fanart_path.strip('.\\'))
        logging.info(f"autogen_marquee CONFIGINI - logo_file_path {logo_file_path} fanart_file_path {fanart_file_path}")

    # Récupération des chemins dans le fichier Gamelist
    if not os.path.exists(logo_file_path) or not os.path.exists(fanart_file_path):
        logging.info(f"PP logo_file_path  {logo_file_path} not exist ->")
        game_info = find_game_info_in_gamelist(game_name, system_name, roms_path)
        if game_info:
            marquee_rel_path = game_info.get('marquee', '').replace('/', '\\')
            fanart_rel_path = game_info.get('fanart', '').replace('/', '\\')
            if marquee_rel_path:
                logo_file_path = os.path.join(roms_path, system_name, marquee_rel_path.strip('.\\'))
            if fanart_rel_path:
                fanart_file_path = os.path.join(roms_path, system_name, fanart_rel_path.strip('.\\'))
        logging.info(f"autogen_marquee GAMELIST - logo_file_path {logo_file_path} fanart_file_path {fanart_file_path}")

    # Vérifier si le logo et le fanart existent
    if os.path.exists(logo_file_path) and os.path.exists(fanart_file_path):
        marquee_width = int(config['Settings']['MarqueeWidth'])
        marquee_height = int(config['Settings']['MarqueeHeight'])
        marquee_border = int(config['Settings']['MarqueeBorder'])

        logo_align, vertical_align = analyze_image(fanart_file_path)

        if current_logo_align is not None:
            logo_align = current_logo_align
        #DMD support format
        if logo_align == 'center' or (marquee_width in [128, 256] and marquee_height in [32, 64]):
            logo_gravity = 'Center'
            logo_position = '+0+0'   # Centré
        elif logo_align == 'left':
            logo_gravity = 'West'
            logo_position = '+50+0'  # Décalage depuis la gauche
        elif logo_align == 'right':
            logo_gravity = 'East'
            logo_position = '+50+0'  # Décalage depuis la droite

        band_half_height = marquee_height // 2
        if vertical_align == 'top':
            fanart_gravity = 'North'
            decy_offset = band_half_height if current_band_decy else 0
        elif vertical_align == 'middle':
            fanart_gravity = 'Center'
            decy_offset = band_half_height if current_band_decy else 0
        elif vertical_align == 'bottom':
            fanart_gravity = 'South'
            decy_offset = band_half_height if current_band_decy else 0

        logging.info(f"fanart_gravity {fanart_gravity} current_band_decy {current_band_decy} decy_offset {decy_offset} band_half_height {band_half_height}")

        intermediate_img_path = target_img_path.replace('.png', '_temp.png')

        # Chargement du logo et récupération de ses dimensions d'origine
        logo_img = Image.open(logo_file_path)
        original_width, original_height = logo_img.size

        # Mappage de l'échelle de zoom (1 à 7) à un facteur (exemple 1.0 à 2.2)
        zoom_scale = {1: 1.0, 2: 1.2, 3: 1.4, 4: 1.6, 5: 1.8, 6: 2.0, 7: 2.2}
        zoom_factor = zoom_scale.get(current_logo_zoom, 1.0)  # Par défaut 1.0 (100%)

        # Calcul du facteur d'échelle pour le logo en fonction du zoom et des dimensions du marquee
        # Cela permet de ne pas dépasser le cadre du marquee
        scale_factor = min(zoom_factor, marquee_width / original_width, marquee_height / original_height)
        logo_max_width = int(original_width * scale_factor)
        logo_max_height = int(original_height * scale_factor)

        # Optionnel : vous pouvez ajouter un léger padding si nécessaire
        # logo_max_height = min(logo_max_height + 10, marquee_height)

        # Gestion du gradient (si utilisé)
        if current_gradient_mode == 2:
            gradient_path = "images/gradient_black.png"
        elif current_gradient_mode == 3:
            gradient_path = "images/gradient_white.png"
        else:
            gradient_path = ""

        if logo_align == 'left':
            gradient_gravity = 'West'
            gradient_center_x = 50 + logo_max_width - (original_width * scale_factor)
        elif logo_align == 'center':
            gradient_gravity = 'Center'
            gradient_center_x = 0
        elif logo_align == 'right':
            gradient_gravity = 'East'
            gradient_center_x = 50 + logo_max_width - (original_width * scale_factor)
        gradient_position = f"+{gradient_center_x}+0"

        # Préparation des commandes ImageMagick selon le fichier config.ini
        convert_command_template = config['Settings']['IMConvertCommandMarqueeGen']
        convert_command = convert_command_template.format(
            IMPath=config['Settings']['IMPath'],
            FanartPath=fanart_file_path,
            FanartGravity=fanart_gravity,
            MarqueeWidth=marquee_width,
            MarqueeHeight=marquee_height,
            DecyOffset=decy_offset,
            IntermediateImgPath=intermediate_img_path,
            MarqueeBackgroundColor=config['Settings']['MarqueeBackgroundColor'],
            ImgTargetPath=target_img_path
        )

        convert_command_template_logo_gradient = config['Settings']['IMConvertCommandMarqueeGenGradientLogo']
        convert_command_logo_gradient = convert_command_template_logo_gradient.format(
            IMPath=config['Settings']['IMPath'],
            LogoMaxWidth=logo_max_width,
            LogoMaxHeight=logo_max_height,
            IntermediateImgPath=intermediate_img_path,
            GradientPath=gradient_path,
            GradientPosition=gradient_position,
            GradientGravity=gradient_gravity
        )

        convert_command_template_logo = config['Settings']['IMConvertCommandMarqueeGenLogo']
        convert_command_logo = convert_command_template_logo.format(
            IMPath=config['Settings']['IMPath'],
            LogoMaxWidth=logo_max_width,
            LogoMaxHeight=logo_max_height,
            IntermediateImgPath=intermediate_img_path,
            LogoPath=logo_file_path,
            LogoGravity=logo_gravity,
            LogoPosition=logo_position,
            ImgTargetPath=target_img_path
        )

        # Exécution des commandes
        subprocess.run(convert_command, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)
        logging.info(f"autogen_marquee convert_command {convert_command}")
        if current_gradient_mode != 1:
            subprocess.run(convert_command_logo_gradient, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)
            logging.info(f"autogen_marquee convert_command_logo_gradient {convert_command_logo_gradient}")
        subprocess.run(convert_command_logo, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)
        logging.info(f"autogen_marquee convert_command_logo {convert_command_logo}")
        os.remove(intermediate_img_path)
        return target_img_path
    else:
        return None


#action=game-start&param1="C:\RetroBatV6\roms\amstradcpc\Back To The Future II (UK) (1990) (Trainer).zip"&param2="Back To The Future II (UK) (1990) (Trainer)"&param3="Back to the Future Part II"
#action=game-selected&param1="amstradcpc"&param2="C:/RetroBatV6/roms/amstradcpc/007 - Live and Let Die (1988)(Domark).zip"&param3="Live and Let Die" // game
#action=system-selected&param1="amstradcpc" // systems
#action=system-selected&param1="all"  event=system-selected&param1="favorites" // collections
def parse_path(action, params, systems_config):
    global current_logo_align, current_logo_zoom, current_gradient_mode
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

    equivalences = {
        '|A': '&',
        '|g': '"',
        '|v': ',',
        '|p': '+',
        '|': '!'
    }
    def replace_special_characters(value):
        for original, replacement in equivalences.items():
            value = value.replace(original, replacement)
        return value

    param1 = replace_special_characters(params.get('param1', ''))
    param2 = replace_special_characters(params.get('param2', ''))
    param3 = replace_special_characters(params.get('param3', ''))
    param4 = replace_special_characters(params.get('param4', ''))

    #special collection groupée
    if param1 == param2 and param2 == param3:
        action = "system-selected"

    logging.info(f"PP clean params - param1 {param1} param2 {param2} param3 {param3} param4 {param4}")

    # GAME FORCE UPDATE
    if action == 'game-forceupdate':
        system_name = param1
        game_name = param2
        game_title = param3
        formatted_rom_path = param4
        return 'game-forceupdate', system_name, game_name, game_title, formatted_rom_path

    if action == 'game-selected' or action == 'system-selected':
        current_band = 2
        current_logo_align = None
        current_logo_zoom = 3
        current_gradient_mode = 1
        system_name = param1
        system_rom_path = os.path.join(roms_path, param1) # C:\RetroBat\roms\<system>
        formatted_rom_path = os.path.normpath(urllib.parse.unquote(param2)) #C:\RetroBat\roms\<system>\<rom.ext>
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
    if action == 'game-start':
        game_name = param2
        game_title = param3
        formatted_rom_path = os.path.normpath(urllib.parse.unquote(param1))
        logging.info(f"PP GAME START formatted_rom_path - {formatted_rom_path}")
        remaining_path = formatted_rom_path.replace(roms_path, "")
        if remaining_path.startswith("\\"):
            remaining_path = remaining_path[1:]
        system_name = remaining_path.split('\\')[0]
        #logging.info(f"PP GAME-START remaining_path : {remaining_path}, system_name : {system_name}")
        logging.info(f"PP GAME-START system_name : {system_name}, game name : {game_name}, game title : {game_title}, rom_path : {formatted_rom_path}")
        return 'game', system_name, game_name, game_title, formatted_rom_path

    # GAME SELECTED
    if action == 'game-selected':
        game_title = param3
        formatted_rom_path = os.path.normpath(urllib.parse.unquote(param2))
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

    # SYSTEM / COLLECTION
    elif action == 'system-selected' :
        if system_folder == True and system_essystems == True:
            system_name = param1
            logging.info(f"PP system : {system_name}, system_folder : {system_folder}, system_essystems {system_essystems}")
            return 'system', system_name, system_folder, system_essystems, ''
        else:
            collection = param1
            logging.info(f"PP collection : {system_name}, system_folder : {system_folder}, system_essystems {system_essystems}")
            return 'collection', collection, system_folder, system_essystems, ''

    return '', '', '', '', ''

last_execution_time = 0  # Timestamp de la dernière exécution
last_command_id = None
current_marquee_file = None
def execute_command(action, params, systems_config):
    global last_execution_time, last_command_id, current_marquee_file
    if action in config['Commands'] or action == "game-forceupdate":
        logging.info(f"EE execute_command {action}")

        current_command_id = f"{action}-{json.dumps(params)}"
        if current_command_id == last_command_id and (time.time() - last_execution_time) < 1 and action != "game-forceupdate":
            logging.info("EE Command skipped as it was executed recently.")
            return json.dumps({"status": "skipped", "message": "Command was executed recently"})

        type, param1, param2, param3, param4 = parse_path(action, params, systems_config)
        # On remplace les caracteres speciaux par les bons pour chercher l'image
        equivalencesParam = {'!p' : '+'}
        def replace_special_characters_params(value):
            # S'assurer que value est une chaîne de caractères
            if not isinstance(value, str):
                return value  # Retourne la valeur telle quelle si ce n'est pas une chaîne

            for original, replacement in equivalencesParam.items():
                value = value.replace(original, replacement)
            return value

        param1=replace_special_characters_params(param1)
        param2=replace_special_characters_params(param2)
        param3=replace_special_characters_params(param3)
        param4=replace_special_characters_params(param4)

        logging.info(f"EE find_marquee_file type {type}, param1 {param1} ,param2 {param2}, param3 {param3} ,param4 {param4}")
        logging.info(f"EE find_fanart_file type {type}, param1 {param1} ,param2 {param2}, param3 {param3} ,param4 {param4}")
        marquee_file = find_marquee_file(type, param1, param2, param3, param4, systems_config)
        fanart_file = find_fanart_file(type, param1, param2, param3, param4, systems_config)
        #if marquee_file == 'marquee_compose':
        #    return json.dumps({"status": "success", "message": "marquee_compose"})
        #escaped_marquee_file = escape_file_path(marquee_file)

        # On remplace les caracteres speciaux par les bons pour executer la commande
        equivalences = {#'^' : '^^',
                        #'&' : '^&',
                        #',' : '^,',
                        #'<' : '^<',
                        #'>' : '^>',
                        #"'" : "^'",
                        '\\': '\\\\'
        }
        def replace_special_characters(value):
            for original, replacement in equivalences.items():
                value = value.replace(original, replacement)
            return value

        marquee_file=replace_special_characters(marquee_file)
        fanart_file=replace_special_characters(fanart_file)

        logging.info(f"EE marquee_file {marquee_file}")
        logging.info(f"EE fanart_file {fanart_file}")

        logging.info(f"EE Vérification de action: {action}")
        logging.info(f"EE Vérification de params: {params}")
        #logging.info(f"EE Vérification de systems_config: {systems_config}")
        logging.info(f"EE Vérification de config['Commands']: {config['Commands']}")
        logging.info(f"EE Vérification de config['Commands'].get(action): {config['Commands'].get(action)}")

        current_marquee_file = marquee_file

        if config['Settings']['MarqueeCompose'] == "true":
            logging.info(f"EE MarqueeCompose : {config['Settings']['MarqueeCompose']}")
            json_command = json.dumps({"command": ["script-message", "change-img", marquee_file, fanart_file]})
            command = f'echo {json_command} > {config["Settings"]["IPCChannel"]}'
            push_datas_to_MPV("marquee-commpose", {"marquee": marquee_file, "fanart": fanart_file})
        else:
            command = config['Commands'].get(action).format(
                    marquee_file=marquee_file,
                    IPCChannel=config['Settings']['IPCChannel']
            )
            subprocess.run(command, shell=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)

            if config['Settings']['ActiveDMD'] == "true":
                #push_datas_to_MPV("marquee-pushtodmd", {"marquee": marquee_file})
                command = config['Commands'].get(action).format(
                    marquee_file=marquee_file,
                    IPCChannel=config['Settings']['IPCChannelDMD']
                )
                subprocess.run(command, shell=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=creation_flags)

        logging.info(f"EE Executing the command : {command}")
        logging.info(f"EE Commande brute avant exécution : {repr(command)}")
        last_command_id = current_command_id
        last_execution_time = time.time()
        return json.dumps({"status": "success", "action": action, "command": command})
    return json.dumps({"status": "error", "message": "No command configured for this action"})

def recursive_clean_slashes(s):
    new_s = re.sub(r'\\+', r'\\\\', s)
    # Si la chaîne ne change plus, on a terminé
    if new_s == s:
        return new_s
    else:
        return recursive_clean_slashes(new_s)

def push_datas_to_MPV(action, datas):
    logging.info(f"Executing push_datas_to_MPV")
    # Joindre les données en une chaîne séparée par des barres verticales
    if isinstance(datas, dict):
        data_str = "|".join([action] + [recursive_clean_slashes(str(value)) for value in datas.values()])
    elif isinstance(datas, (list, tuple)):
        data_str = "|".join([action] + [recursive_clean_slashes(str(item)) for item in datas])
    else:
        data_str = f"{action}|{str(datas)}"


    # Récupération de la commande depuis le fichier de configuration (RA historiquement)
    command_template = config['Commands'][action]
    if command_template:
        # Remplacement du placeholder {data} par la chaîne
        command = command_template.replace("{IPCChannel}", config['Settings']['IPCChannel'])
        command = command.replace("{data}", data_str)

        # Exécution de la commande
        try:
            subprocess.run(command, shell=True, check=True)
            logging.info(f"Commande push_datas_to_MPV exécutée avec succès : {command}")
            return True
        except Exception as e:
            logging.error(f"Erreur lors de l'exécution de la commande push_datas_to_MPV : {e}")
            return False
    else:
        logging.error("La commande MPVPushRetroAchievementsDatas n'est pas définie dans le fichier de configuration.")
        return False

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
    try:
        with open(file_path, 'r') as file:
            content = file.read().strip()
        logging.info(f"on_file_modified content : {content}")
        params = urllib.parse.parse_qs(content)
        action = params.get('event', [''])[0]  # Prend le premier élément ou une chaîne vide

        # Nettoyer les paramètres
        params.pop('event', None)
        for key, value in params.items():
            cleanvalue = value[0].strip(' "')
            logging.info(f"#>>> params.items key : {key}, value : {value}, cleanvalue : {cleanvalue}")
            params[key] = cleanvalue

        logging.info(f"Action received : {action}, Parameters : {params} --")

        # Gestion du flag : mémoriser qu'un game-start a eu lieu
        global game_start_occurred
        if action == 'game-start' and config['Settings']['ActiveDMD'] == "true":
            game_start_occurred = True
        # Lors d'un game-selected, ne lancer la vérification de dmd que si un game-start est intervenu auparavant
        elif action == 'game-selected' and config['Settings']['ActiveDMD'] == "true":
            if game_start_occurred and config['Settings']['ActiveDMD'] == "true":
                threading.Thread(target=check_and_launch_dmd, daemon=True).start()
            # Une fois géré, on réinitialise le flag pour éviter des déclenchements multiples
            game_start_occurred = False

        # Vous pouvez aussi, si besoin, réinitialiser le flag pour un éventuel "game-end" :
        elif action == 'game-end' and config['Settings']['ActiveDMD'] == "true":
            game_start_occurred = False

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

import keyboard
current_band = 2
current_logo_zoom = 3
current_gradient_mode = 1
current_logo_align = None
current_band_decy = False
def on_pressed(key):
    global current_band, current_logo_align, current_band_decy, current_logo_zoom, current_gradient_mode
    action = ''
    if key.name == 'f6':
        current_gradient_mode = current_gradient_mode + 1
        if current_gradient_mode > 3:
            current_gradient_mode = 1
        action = 'game-forceupdate'
    elif key.name == 'f7':
        current_logo_zoom = current_logo_zoom + 1
        if current_logo_zoom > 6:
            current_logo_zoom = 1
        action = 'game-forceupdate'
    elif key.name == 'f8':
        current_band_decy = not current_band_decy
        action = 'game-forceupdate'
    elif key.name == 'f9':
        current_band = current_band + 1
        if current_band > 3:
            current_band = 1
        action = 'game-forceupdate'
    elif key.name in ['f10', 'f11', 'f12']:
        current_logo_align = 'left' if key.name == 'f10' else 'center' if key.name == 'f11' else 'right'
        action = 'game-forceupdate'

    if action == 'game-forceupdate':
        # Affiche le message de l'action
        command = config['Settings']['MPVShowText'].format(
            message=f"Action: {action}, Band: {current_band}, Align: {current_logo_align}, Zoom: {current_logo_zoom}, Gradient Mode: {current_gradient_mode}",
            IPCChannel=config['Settings']['IPCChannel']
        )
        subprocess.run(command, shell=True)

        # Prépare les paramètres pour l'action
        params = {
            'param1': current_system_name,
            'param2': current_game_name,
            'param3': current_game_title,
            'param4': current_rom_path
        }
        logging.info(f"{action} pressed, params: {params}")
        execute_command(action, params, systems_config)

def keyboard_listener():
    keyboard.on_press(on_pressed)
    keyboard.wait()

if __name__ == '__main__':
    load_config()
    #systems_config = load_systems_config(os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'es_systems.cfg'))
    systems_config_directory = os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation')
    systems_config = load_all_systems_configs(systems_config_directory)
    lock = threading.Lock()

    # Démarrer la surveillance du fichier
    file_thread = threading.Thread(target=start_watching)
    file_thread.start()
    logging.info(f"File watching thread started: {file_thread.is_alive()}")

    # Thread pour l'écoute du clavier
    keyboard_thread = threading.Thread(target=keyboard_listener)
    keyboard_thread.start()

    launch_media_player()

    if config['Settings']['MarqueeRetroAchievements'] == "true":
       launch_process("ESRetroAchievements.exe")
    if config['Settings']['MarqueeAutoScraping'] == "true":
       launch_process("ESEventsScrapTopper.exe")
    if config['Settings']['MarqueePinballDMD'] == "true":
       launch_process("VPListenerWS.exe")
    if config['Settings']['MarqueeMameOutput'] == "true":
       launch_process("MAMEListenerWS.exe")
    if config['Settings']['ActiveDMD'] == "true":
       launch_process("dmd/dmd.exe")
