import configparser
import xml.etree.ElementTree as ET
import requests
import json
import logging

def get_systems_from_api(es_settings):
    api_url = "https://api.screenscraper.fr/api2/systemesListe.php"
    params = {
        'devid': 'Nelfe',
        'devpassword': 'y8dI5PyYsyM',
        'softname': 'ESEventsScrapTopper',
        'output': 'json',
        'ssid': es_settings['ScreenScraperUser'],
        'sspassword': es_settings['ScreenScraperPass'],
    }

    try:
        response = requests.get(api_url, params=params)
        if response.status_code == 200:
            logging.info(f"SCREENSCRAPER API SYSTEMS LOADING")  # Pour le débogage
            return response.json()
        else:
            logging.error("Échec de l'appel API: " + response.text)
            return None
    except requests.RequestException as e:
        logging.error("Erreur lors de l'appel API: " + str(e))
        return None

def load_es_settings(xml_relative_path):
    script_dir = os.path.dirname(os.path.realpath(__file__))
    xml_path = os.path.join(script_dir, xml_relative_path)
    tree = ET.parse(xml_path)
    root = tree.getroot()

    es_settings = {}

    for setting in root:
        if setting.tag == 'string':
            key = setting.get('name')
            value = setting.text
            es_settings[key] = value
            logging.info(f"ES Setting {key} loaded with value - {value}")
        else:
            logging.info(f"Ignoring non-string setting: {setting.tag}")
    logging.info(f"ESSETTINGS LOADING")  # Pour le débogage
    return es_settings

def load_systems_config(xml_relative_path):
    script_dir = os.path.dirname(os.path.realpath(__file__))
    xml_path = os.path.join(script_dir, xml_relative_path)
    tree = ET.parse(xml_path)
    root = tree.getroot()
    system_folders = {}

    for system in root.findall('system'):
        if system.find('name') is not None and system.find('platform') is not None:
            name = system.find('name').text
            platform = system.find('platform').text
            system_folders[name] = platform
            #logging.info(f"System {name} - platform : {platform}")
        else:
            logging.info(f"Missing name")
    #logging.info(f"system_folders {system_folders}")
    logging.info(f"ESSYSTEMS LOADING")  # Pour le débogage
    return system_folders

def load_config():
    config = configparser.ConfigParser()
    config.read('events.ini')
    return config

def load_screenscraper_config():
    config = configparser.ConfigParser()
    config.read('screenscraper.ini')
    ss_dict = {}
    if 'Dictionnary' in config:
        for key, value in config['Dictionnary'].items():
            ss_dict[key.lower()] = value.lower()
    logging.info(f"DICTIONNARY LOADING screenscaper.ini")  # Pour le débogage
    return ss_dict

def create_systems_scrap_file(data, es_systems, ss_config):
    systems_dict = {}
    added_systems = set()
    logging.info(f"CREATING FILE systems.scrap")
    for system in data['response']['systemes']:
        system_id = system['id']
        eu_name = system['noms'].get('nom_eu', '').lower()
        recalbox_names = system['noms'].get('nom_recalbox', '').split(',')
        retropie_names = system['noms'].get('nom_retropie', '').split(',')
        logging.info(f"####### ADD {eu_name}")
        # Ajouter le nom européen (nom_eu) à systems_dict
        if eu_name:
            systems_dict[eu_name] = system_id
            logging.info(f"ADD1 systems_dict[{eu_name}] : {system_id}")
            added_systems.add(eu_name)

        # Ajouter le nom du dictionnaire selon le nom européen
        if eu_name and ss_config.get(eu_name, eu_name) not in added_systems:
            mapped_name = ss_config.get(eu_name, eu_name)
            systems_dict[mapped_name] = system_id
            logging.info(f"ADD2 systems_dict[{mapped_name}] : {system_id}")
            added_systems.add(eu_name)

        # Gérer les noms alternatifs et les mappages
        for name in recalbox_names + retropie_names:
            name = name.strip().lower()
            if name and name not in added_systems:
                mapped_name = ss_config.get(name, name)
                systems_dict[mapped_name] = system_id
                logging.info(f"ADD3  systems_dict[{mapped_name}] : {system_id}")
                added_systems.add(mapped_name)
        logging.info(f"####### FIN ADD {eu_name}")
    with open('systems.scrap', 'w') as file:
        json.dump(systems_dict, file, indent=4)


def main():
    config = load_config()
    ss_config = load_screenscraper_config()
    es_settings = load_es_settings(os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'es_settings.cfg'))
    es_systems = load_systems_config(os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'es_systems.cfg'))
    data = get_systems_from_api(es_settings)
    if data:
        create_systems_scrap_file(data, es_systems, ss_config)

if __name__ == "__main__":
    main()
