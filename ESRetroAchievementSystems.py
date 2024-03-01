import configparser
import xml.etree.ElementTree as ET
import requests
import json
import logging
import os

def load_systems_config(xml_relative_path):
    script_dir = os.path.dirname(os.path.realpath(__file__))
    xml_path = os.path.join(script_dir, xml_relative_path)
    tree = ET.parse(xml_path)
    root = tree.getroot()
    system_folders = {}

    for system in root.findall('system'):
        name = system.find('name').text
        fullname = system.find('fullname').text
        system_folders[name] = fullname
    logging.info("ESSYSTEMS LOADING")
    return system_folders

def load_ra_mapping(ini_file_path):
    config = configparser.ConfigParser()
    config.read(ini_file_path)
    ra_mapping = {}
    if 'Dictionnary' in config:
        ra_mapping = config['Dictionnary']
    else:
        logging.error("La section 'Dictionnary' est manquante dans le fichier retroachievements.ini.")
    return ra_mapping
	
def load_es_settings(xml_file_path):
    tree = ET.parse(xml_file_path)
    root = tree.getroot()
    es_settings = {}

    for setting in root.findall('string'):
        name = setting.get('name')
        if name in ['global.retroachievements.username', 'global.retroachievements.token']:
            es_settings[name] = setting.get('value')

    if 'global.retroachievements.username' not in es_settings or 'global.retroachievements.token' not in es_settings:
        logging.error("Les clés d'identification RetroAchievements sont manquantes dans es_settings.cfg.")
        return None

    return es_settings
	
def get_ra_systems(es_settings):
    api_url = "https://retroachievements.org/API/API_GetConsoleIDs.php"
    params = {
        'z': 'Nelfe',
        'y': 'WPg4ngbUsJdtF5Pk97OQ91Mle0FnGQqx'
    }
    try:
        response = requests.get(api_url, params=params)
        if response.status_code == 200:
            return response.json()
        else:
            logging.error("Échec de l'appel API RetroAchievements: " + response.text)
            return None
    except requests.RequestException as e:
        logging.error("Erreur lors de l'appel API RetroAchievements: " + str(e))
        return None

def create_systems_retro_mapping(es_systems, ra_systems, ra_mapping):
    mapping = {}

    for es_name, es_fullname in es_systems.items():
        # Extraire le nom de l'API du dictionnaire
        ra_full_mapping = ra_mapping.get(es_fullname, es_fullname)
        ra_name_parts = ra_full_mapping.split(' = ')
        ra_name = ra_name_parts[-1].lower() if len(ra_name_parts) > 1 else ra_full_mapping.lower()

        print(f"Mapping: {es_fullname} -> {ra_name}")  # Vérification du mapping

        # Trouver l'ID correspondant dans l'API RetroAchievements
        ra_system_id = next((str(system['ID']) for system in ra_systems if system['Name'].lower() == ra_name), 'undefined')

        mapping[es_name] = ra_system_id
        print(f"ES System: {es_name}, Fullname: {es_fullname}, RA Name: {ra_name}, RA ID: {ra_system_id}")

    return mapping


def save_systems_retro_mapping(systems_retro, filename='systems.retro'):
    with open(filename, 'w') as file:
        json.dump(systems_retro, file, indent=4)
    logging.info("Fichier systems.retro créé")

def main():
    logging.basicConfig(level=logging.INFO)
    config = configparser.ConfigParser()
    config.read('events.ini')
    ra_mapping = load_ra_mapping('retroachievements.ini')
    es_settings = load_es_settings(os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'es_settings.cfg'))
    es_systems = load_systems_config(os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'es_systems.cfg'))
    ra_systems = get_ra_systems(es_settings)
    if ra_systems and ra_mapping:
        systems_retro_mapping = create_systems_retro_mapping(es_systems, ra_systems, ra_mapping)
        save_systems_retro_mapping(systems_retro_mapping)

if __name__ == "__main__":
    main()
