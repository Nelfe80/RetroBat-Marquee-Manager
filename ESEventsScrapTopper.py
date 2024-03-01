import configparser
import os
import time
import xml.etree.ElementTree as ET
import logging
import requests
import zlib
import json
import socket
import subprocess

class ScreenScraperAPI:
    def __init__(self, api_params, systems_dict):
        self.base_url = "https://api.screenscraper.fr/api2"
        self.api_params = api_params
        self.systems_dict = systems_dict

    def getSystemId(self, system_name):
        return self.systems_dict.get(system_name.lower())

    def callAPI(self, endpoint, additional_params={}):
        # Fusionner les paramètres API de base et les paramètres supplémentaires
        params = {**self.api_params, **additional_params}
        response = requests.get(f"{self.base_url}/{endpoint}.php", params=params)
        url = f"{self.base_url}/{endpoint}.php?" + "&".join([f"{key}={params[key]}" for key in params])
        if response.status_code != 200:
            logging.error(f"API call failed: " + response.text)
            return None, response.text
        logging.debug(f"API response: {response.json()}")
        return response.json(), ''

    def getMarquee(self, systemeid, romnom):
        response, error_message = self.callAPI("jeuInfos", {"systemeid": systemeid, "romnom": romnom})
        if response and "response" in response:
            jeu = response["response"].get("jeu")
            if jeu and "medias" in jeu:
                for media in jeu["medias"]:
                    if media["type"] == "marquee":
                        return media, ''
                    elif media["type"] == "screenmarquee":
                        return media, ''
        return None, error_message

    def downloadAndSaveImage(self, image_url, save_path, marquee_format):
        save_url_marquee=f"{save_path}.{marquee_format}"
        if os.path.exists(save_url_marquee):
            return True
        try:
            # Vérifier si le dossier existe, sinon le créer
            directory = os.path.dirname(save_url_marquee)
            if not os.path.exists(directory):
                os.makedirs(directory)
            response = requests.get(image_url, timeout=10)  # Ajouter un délai d'expiration
            if response.status_code == 200:
                with open(save_url_marquee, 'wb') as file:
                    file.write(response.content)
                return True
            else:
                return False
        except requests.RequestException as e:
            logging.info(f"Erreur lors du téléchargement de l'image : {e}")
            return False
        except Exception as e:
            logging.info(f"Erreur lors de la création du fichier : {e}")
            return False

# Fonction pour charger les paramètres de connexion ScreenScraper depuis es_settings.cfg
def load_es_settings(xml_relative_path):
    script_dir = os.path.dirname(os.path.realpath(__file__))
    xml_path = os.path.join(script_dir, xml_relative_path)
    tree = ET.parse(xml_path)
    root = tree.getroot()

    es_settings = {}

    for setting in root.findall('string'):
        key = setting.get('name')
        value = setting.get('value')  # Utilisez get('value') pour récupérer la valeur
        es_settings[key] = value
        logging.info(f"ES Setting {key} loaded with value - {value}")

    return es_settings

def load_systems_scrap(file_path):
    with open(file_path, 'r') as file:
        systems_dict = json.load(file)
    return systems_dict

def md5(rom_path):
    try:
        hash_md5 = hashlib.md5()
        with open(rom_path, "rb") as f:
            for chunk in iter(lambda: f.read(4096), b""):
                hash_md5.update(chunk)
        md5_hash = hash_md5.hexdigest().upper()
        logging.debug(f"Calculated MD5 for {rom_path}: {md5_hash}")
        return md5_hash
    except Exception as e:
        logging.error(f"Could not calculate MD5 for {rom_path}: {e}")
        return ""

def sha1(rom_path):
    try:
        hasher = hashlib.sha1()
        with open(rom_path, 'rb') as file:
            buf = file.read(65536)  # Lecture par blocs de 65536 octets
            while len(buf) > 0:
                hasher.update(buf)
                buf = file.read(65536)
        sha1_hash = hasher.hexdigest().upper()
        logging.debug(f"SHA1 calculated for {rom_path}: {sha1_hash}")
        return sha1_hash
    except Exception as e:
        logging.error(f"Could not calculate SHA1 for {rom_path}: {e}")
        return ""

def crc(rom_path):
    try:
        prev = 0
        with open(rom_path, 'rb') as file:
            for eachLine in file:
                prev = zlib.crc32(eachLine, prev)
        crc_value = "%X" % (prev & 0xFFFFFFFF)
        logging.debug(f"CRC calculated for {rom_path}: {crc_value}")
        return crc_value.upper()
    except Exception as e:
        logging.error(f"Could not calculate CRC for {rom_path}: {e}")
        return ""

def size(rom_path):
    total_size = 0
    if os.path.isfile(rom_path):
        # Si c'est un fichier, obtenez simplement sa taille
        total_size = os.path.getsize(rom_path)
    elif os.path.isdir(rom_path):
        # Si c'est un dossier, calculez la taille de tous les fichiers à l'intérieur
        for dirpath, dirnames, filenames in os.walk(rom_path):
            for f in filenames:
                fp = os.path.join(dirpath, f)
                if os.path.isfile(fp):
                    total_size += os.path.getsize(fp)
    return total_size

# Fonction pour le scrapping
def scrape_marquee(game_system, game_title, game_name, marquee_path, full_marquee_path, rom_path, es_settings, systems_dict, config):
    logging.debug(f"Scraping marquee for: {game_system}, {game_title}, {game_name}, {marquee_path}, {full_marquee_path}, {rom_path}")

    for fmt in config['Settings']['AcceptedFormats'].split(','):
        full_path = f"{full_marquee_path}.{fmt.strip()}"
        logging.debug(f"Test si {full_path} existe")
        if os.path.exists(full_path):
            logging.debug(f"Le fichier existe déjà : {full_path}")
            return True, "Marquee File exist"

    # Calcul des hash pour l'appel API
    #md5_hash = md5(rom_path)
    #sha1_hash = sha1(rom_path)
    crc_hash = crc(rom_path)
    rom_size = size(rom_path)
    rom = os.path.basename(rom_path)

    # Préparation des paramètres pour l'appel API
    api_params = {
        'devid': '',
        'devpassword': '',
        'softname': 'Retrobat-Marquee-Manager-v.3.0',
        'output': 'json',
        'ssid': es_settings['ScreenScraperUser'],
        'sspassword': es_settings['ScreenScraperPass'],
        #'md5': md5_hash,
        #'sha1': sha1_hash,
        'crc': crc_hash,
        'romtaille': rom_size,
        'romnom': rom,
        'romtype': 'rom'
    }

    scraper = ScreenScraperAPI(api_params, systems_dict)

    logging.info(f"API SYSTEM_ID: {game_system}")
    system_id = scraper.getSystemId(game_system)

    if system_id:
        logging.debug(f"SYSTEM_ID trouvé: {system_id}")
        logging.debug(f"L'identifiant du système pour '{game_system}' est {system_id}")
    else:
        logging.debug(f"PAS DE SYSTEM_ID trouvé: {game_system}")
        logging.debug(f"Aucun identifiant trouvé pour le système '{game_system}'")
        return False, "No System find"

    logging.debug(f"API MARQUEE: {system_id} {rom}")
    marquee_media, error_message = scraper.getMarquee(system_id, rom)

    if marquee_media:
        logging.debug(f"full_marquee_path: {full_marquee_path}")
        if scraper.downloadAndSaveImage(marquee_media['url'], full_marquee_path, marquee_media['format']):
            logging.debug(f"URL du marquee trouvé: {marquee_media['url']}.{marquee_media['format']}")
            command = config['Settings']['MPVShowText'].format(
                                    message=(f"{game_title.replace('+', ' ')} topper successfully scraped!"),
                                    IPCChannel=config['Settings']['IPCChannel']
                                )
            subprocess.run(command, shell=True)
            return True, "Marquee find"
    else:
        logging.debug(f"Pas de marquee trouvé {game_title}, {game_name}")
        return False, (f"No marquee find - API error: {error_message}")

# Fonction pour supprimer une ligne traitée du fichier scrap.pool
def remove_scrap_request(pool_file, line_to_remove):
    with open(pool_file, "r") as file:
        lines = file.readlines()
    with open(pool_file, "w") as file:
        for line in lines:
            if line.strip() != line_to_remove:
                file.write(line)

def write_scrap_failed(failed_pool_file, failed_line):
    with open(failed_pool_file, "a") as file:
        file.write(failed_line + '\n')

config = configparser.ConfigParser()
def load_config():
    global config
    config.read('config.ini')
    current_working_dir = os.getcwd()  # C:\RetroBat\plugins\MarqueeManager\
    logging.info(f"{current_working_dir}")
    def update_path(setting, default_path):
        logging.info(f"update_path {setting} {default_path}")
        # Vérifier si la variable n'existe pas ou n'est pas un lien absolu
        if setting not in config['Settings'] or not os.path.isabs(config['Settings'].get(setting, '')):
            config['Settings'][setting] = default_path

    update_path('RetroBatPath', os.path.dirname(os.path.dirname(current_working_dir)))
    update_path('RomsPath', os.path.join(config['Settings']['RetroBatPath'], 'roms'))
    update_path('DefaultImagePath', os.path.join(current_working_dir, 'images', 'default.png'))
    update_path('MarqueeImagePath', os.path.join(current_working_dir, 'images'))
    update_path('MarqueeImagePathDefault', os.path.join(config['Settings']['RetroBatPath'], 'roms'))
    update_path('SystemMarqueePath', os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'themes', 'es-theme-carbon-master', 'art', 'logos'))
    update_path('CollectionMarqueePath', os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'themes', 'es-theme-carbon-master', 'art', 'logos'))
    update_path('MPVPath', os.path.join(current_working_dir, 'mpv', 'mpv.exe'))
    update_path('IMPath', os.path.join(current_working_dir, 'imagemagick', 'convert.exe'))

def read_scrap_pool(pool_file):
    with open(pool_file, 'r') as file:
        lines = file.readlines()
    return [line.strip().split('|') for line in lines]

def ensure_file_exists(file_path):
    if not os.path.exists(file_path):
        with open(file_path, 'w') as file:
            pass

# Fonction principale
def main():
    logging.basicConfig(level=logging.INFO)
    load_config()
    es_settings = load_es_settings(os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'es_settings.cfg'))
    logging.debug(f"es_settings: {es_settings}")
    systems_dict = load_systems_scrap(os.path.join(config['Settings']['RetroBatPath'], 'plugins', 'MarqueeManager', 'systems.scrap'))
    scrap_pool_file = os.path.join(config['Settings']['RetroBatPath'], 'plugins', 'MarqueeManager', 'scrap.pool')
    scrap_failed_file = os.path.join(config['Settings']['RetroBatPath'], 'plugins', 'MarqueeManager', 'scrapfailed.pool')

    ensure_file_exists(scrap_pool_file)
    ensure_file_exists(scrap_failed_file)

    while True:
        scrap_requests = read_scrap_pool(scrap_pool_file)
        logging.debug(f"scrap_requests: {scrap_requests}")
        for request in scrap_requests:
            line = '|'.join(request)
            game_system, game_title, game_name, marquee_path, full_marquee_path, rom_path = request
            logging.debug(f"full_marquee_path: {full_marquee_path}")
            success, error_message = scrape_marquee(game_system.strip(), game_title.strip(), game_name.strip(), marquee_path.strip(), full_marquee_path.strip(), rom_path.strip(), es_settings, systems_dict, config)
            if success:
                remove_scrap_request(scrap_pool_file, line)
            else:
                remove_scrap_request(scrap_pool_file, line)
                failed_line = f"{line}|Error : {error_message}"
                if config['Settings']['MarqueeAutoScrapingDebug'] == "true":
                    write_scrap_failed(scrap_failed_file, failed_line)
        time.sleep(1)

if __name__ == "__main__":
    main()
