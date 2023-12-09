from flask import Flask, request
import configparser
import subprocess
import os
import json
import urllib.parse
import shlex
import xml.etree.ElementTree as ET

app = Flask(__name__)
config = configparser.ConfigParser()

def load_config():
    config.read('events.ini')
    print("Configuration chargée.")

def load_systems_config(xml_relative_path):
    script_dir = os.path.dirname(os.path.realpath(__file__))
    xml_path = os.path.join(script_dir, xml_relative_path)
    tree = ET.parse(xml_path)
    root = tree.getroot()

    system_folders = {}

    for system in root.findall('system'):
        name = system.find('name').text
        path = system.find('path').text
        folder_rom_name = os.path.basename(os.path.normpath(path.strip('~\\..')))
        system_folders[name] = folder_rom_name

    return system_folders

def launch_media_player():
    kill_command = config['Settings']['MPVKillCommand']
    subprocess.run(kill_command, shell=True)
    print(f"Commande de kill exécutée : {kill_command}")

    launch_command = config['Settings']['MPVLaunchCommand'].format(
        MPVPath=config['Settings']['MPVPath'],
        IPCChannel=config['Settings']['IPCChannel'],
        ScreenNumber=config['Settings'].get('ScreenNumber', '1'),
        DefaultImagePath=config['Settings']['DefaultImagePath']
    )
    subprocess.Popen(launch_command, shell=True)
    print(f"Commande de lancement de MPV exécutée : {launch_command}")

def is_mpv_running():
    try:
        test_command = config['Settings']['MPVTestCommand'].format(IPCChannel=config['Settings']['IPCChannel'])
        subprocess.run(test_command, shell=True, check=True)
        print("MPV est en cours d'exécution.")
        return True
    except subprocess.CalledProcessError:
        print("MPV n'est pas en cours d'exécution.")
        return False

def ensure_mpv_running():
    if not is_mpv_running():
        launch_media_player()

def escape_file_path(path):
    return shlex.quote(path)

def find_marquee_file(system_name, game_name, systems_config):
    folder_rom_name = systems_config.get(system_name, system_name)

    marquee_structure = config['Settings']['MarqueeFilePath']
    marquee_path = marquee_structure.format(system_name=folder_rom_name, game_name=game_name)
    full_marquee_path = os.path.join(config['Settings']['MarqueeImagePath'], marquee_path)
    print(f"Chemin du marquee du jeu : {full_marquee_path}")
    marquee_file = find_file(full_marquee_path)
    if marquee_file:
        print(f"Marquee du jeu trouvé : {marquee_file}")
        return marquee_file

    marquee_structure = config['Settings']['SystemFilePath']
    marquee_path = marquee_structure.format(system_name=folder_rom_name)
    full_marquee_path = os.path.join(config['Settings']['SystemMarqueePath'], marquee_path)
    print(f"Chemin du marquee du système : {full_marquee_path}")
    marquee_file = find_file(full_marquee_path)
    if marquee_file:
        print(f"Marquee du système trouvé : {marquee_file}")
        return marquee_file

    print(f"Utilisation de l'image par défaut : {config['Settings']['DefaultImagePath']}")
    return config['Settings']['DefaultImagePath']

def find_file(base_path):
    for fmt in config['Settings']['AcceptedFormats'].split(','):
        full_path = f"{base_path}.{fmt.strip()}"
        if os.path.isfile(full_path):
            print(f"Fichier trouvé : {full_path}")
            return full_path
    print(f"Aucun fichier trouvé pour : {base_path}")
    return None

def parse_path(params, systems_config):
    system_detected = False
    game_detected = False
    for param in params.values():
        decoded_param = urllib.parse.unquote_plus(param)
        print(f"Paramètre décodé : {decoded_param}")
        formatted_path = os.path.normpath(decoded_param)
        print(f"Chemin formaté : {formatted_path}")

        folder_rom_name = systems_config.get(decoded_param, '')
        if os.path.isdir(os.path.join(config['Settings']['RomsPath'], folder_rom_name)):
            print(f"Nom du système détecté : {decoded_param}")
            system_detected = True
            system_name = decoded_param

        if os.path.isfile(formatted_path):
            game_detected = True
            path_parts = formatted_path.split(os.sep)
            game_name = os.path.splitext(os.path.basename(formatted_path))[0]
            system_name = path_parts[-2] if len(path_parts) > 1 else ''
            print(f"Nom du système : {system_name}, Nom du jeu : {game_name}")
            return system_name, game_name

    if system_detected:
        return system_name, ''

    if not game_detected and not system_detected and params:
        first_param = next(iter(params.values()))
        print(f"Utilisation du premier paramètre comme chaîne : {first_param}")
        return '', first_param

    print("Aucun chemin de fichier valide trouvé dans les paramètres.")
    return '', ''

def execute_command(action, params, systems_config):
    if action in config['Commands']:
        system_name, game_name = parse_path(params, systems_config)
        marquee_file = find_marquee_file(system_name, game_name, systems_config)
        escaped_marquee_file = escape_file_path(marquee_file)
        command = config['Commands'][action].format(
            marquee_file=escaped_marquee_file,
            IPCChannel=config['Settings']['IPCChannel']
        )
        print(f"Exécution de la commande : {command}")
        subprocess.run(command, shell=True)
        return json.dumps({"status": "success", "action": action, "command": command})
    return json.dumps({"status": "error", "message": "No command configured for this action"})

@app.route('/', methods=['GET'])
def handle_request():
    ensure_mpv_running()
    action = request.args.get('event', '')
    params = dict(request.args)
    print(f"Action reçue : {action}, Paramètres : {params}")
    params.pop('event', None)
    systems_config = load_systems_config(os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'es_systems.cfg'))
    return execute_command(action, params, systems_config)

if __name__ == '__main__':
    load_config()
    launch_media_player()
    app.run(host=config['Settings']['Host'], port=int(config['Settings']['Port']), debug=False)
