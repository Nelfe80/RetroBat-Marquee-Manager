#!/usr/bin/env python
import socket
import time
import configparser
import subprocess
import os
import json
import urllib.parse
import shlex
import logging
import re

# --- Début des fonctions issues d'ESEvents.py ---

# Pour Windows, on définit un flag pour lancer des processus sans fenêtre
creation_flags = 0
if os.name == "nt":
    creation_flags = subprocess.CREATE_NO_WINDOW

# Configuration globale
config = configparser.ConfigParser()

def load_config():
    global config
    config.read('config.ini')
    if config['Settings']['logFile'] == "true":
        logging.basicConfig(filename="ESEvents.log", level=logging.INFO)
        logging.getLogger('werkzeug').setLevel(logging.INFO)
        logging.info("Start logging")

    current_working_dir = os.getcwd()  # Exemple : C:\RetroBat\plugins\MarqueeManager\

    def update_path(setting, default_path):
        logging.info(f"update_path {setting} {default_path}")
        if setting not in config['Settings'] or not os.path.isabs(config['Settings'].get(setting, '')):
            config['Settings'][setting] = default_path

    logging.info(f"Current working dir: {current_working_dir}")
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
    logging.info("Configuration loaded.")

def recursive_clean_slashes(s):
    new_s = re.sub(r'\\+', r'\\\\', s)
    return new_s if new_s == s else recursive_clean_slashes(new_s)

def push_datas_to_MPV(action, datas):
    logging.info("Executing push_datas_to_MPV")
    if isinstance(datas, dict):
        data_str = "|".join([action] + [recursive_clean_slashes(str(value)) for value in datas.values()])
    elif isinstance(datas, (list, tuple)):
        data_str = "|".join([action] + [recursive_clean_slashes(str(item)) for item in datas])
    else:
        data_str = f"{action}|{str(datas)}"

    command_template = config['Commands'][action]
    if command_template:
        command = command_template.replace("{IPCChannel}", config['Settings']['IPCChannel'])
        command = command.replace("{data}", data_str)
        try:
            subprocess.run(command, shell=True, check=True, creationflags=creation_flags)
            logging.info(f"Commande push_datas_to_MPV exécutée avec succès : {command}")
            return True
        except Exception as e:
            logging.error(f"Erreur lors de l'exécution de la commande push_datas_to_MPV : {e}")
            return False
    else:
        logging.error("La commande pour action '{}' n'est pas définie dans la configuration.".format(action))
        return False

# --- Fin des fonctions issues d'ESEvents.py ---


# --- Code de MAMEListenerWS.py ---

def main():
    # Charger la configuration et initialiser les paramètres
    load_config()

    # Constantes de connexion à MAME
    server_address = ('127.0.0.1', 8000)
    reconnection_delay = 5  # secondes avant reconnexion

    while True:
        try:
            # Création d'une socket TCP/IP
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            print("Attempting to connect to MAME server on port 8000...")
            sock.connect(server_address)
            print("Connected to MAME server.")

            # Envoi des commandes pour obtenir des informations de MAME
            # (Les commandes sont envoyées avec le délimiteur \1 comme attendu par MAME)
            commands = ['send_id = 0\1', 'send_id = 1\1', 'send_id = 2\1']
            for command in commands:
                print(f"Sending command: {command}")
                sock.sendall(command.encode('utf-8'))

            # Réception et traitement des réponses de MAME
            while True:
                data = sock.recv(1024)
                if data:
                    # Affichage des données brutes pour le débogage
                    print("Raw data received:", repr(data))

                    try:
                        decoded_data = data.decode('utf-8')
                        print("Decoded data:", decoded_data)
                    except UnicodeDecodeError as e:
                        print("Error decoding data:", e)
                        continue

                    # Les messages semblent être terminés par un retour chariot (\r)
                    messages = decoded_data.split('\r')
                    for message in messages:
                        if message:
                            print(f"Received message: {message}")
                            # Envoi du message à MPV via push_datas_to_MPV.
                            # Utilisation de "marquee-mame" pour correspondre à la configuration dans config.ini.
                            push_datas_to_MPV("marquee-mame", message)
                        if message.lower().startswith("mame_start"):
                            # Prépare les données : largeur|hauteur
                            dimensions_data = f"width={config['Settings']['MarqueeWidth']}|height={config['Settings']['MarqueeHeight']}"
                            push_datas_to_MPV("marquee-mame", dimensions_data)
                else:
                    print("Connection closed by MAME.")
                    break

        except socket.error as e:
            print(f"Socket error: {e}")

        finally:
            sock.close()
            print("Disconnected from MAME server.")

        print(f"Waiting {reconnection_delay} seconds before reconnecting...")
        time.sleep(reconnection_delay)

if __name__ == "__main__":
    main()
