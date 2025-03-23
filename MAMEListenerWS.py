#!/usr/bin/env python
import socket
import time
import configparser
import subprocess
import os
import logging
import re
import select

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

def process_message(message, last_states):
    """Traite un message en comparant l'état et en envoyant à MPV si nécessaire."""
    print(f"Message traité: {message}")
    match = re.match(r'\s*(\w+)\s*=\s*(\d+)\s*', message)
    if match:
        key = match.group(1)
        value = match.group(2)
        if key in last_states and last_states[key] == value:
            print(f"État inchangé pour {key} : {value}, pas d'envoi.")
        else:
            last_states[key] = value
            push_datas_to_MPV("marquee-mame", message)
    else:
        push_datas_to_MPV("marquee-mame", message)

    if message.lower().startswith("mame_start"):
        dimensions_data = f"width={config['Settings']['MarqueeWidth']}|height={config['Settings']['MarqueeHeight']}"
        push_datas_to_MPV("marquee-mame", dimensions_data)
        push_datas_to_MPV("marquee-mame", dimensions_data)

def find_repeating_block(messages):
    """
    Pour une liste de messages, détermine le bloc minimal qui se répète.
    Par exemple, si messages == [A, B, C, A, B, C], retourne [A, B, C].
    Si aucun bloc répétitif n'est détecté, retourne messages.
    """
    n = len(messages)
    if n <= 1:
        return messages
    for p in range(1, n + 1):
        if n % p == 0:
            block = messages[:p]
            if block * (n // p) == messages:
                return block
    return messages

def main():
    load_config()
    server_address = ('127.0.0.1', 8000)
    reconnection_delay = 5  # secondes avant reconnexion
    last_states = {}
    last_sequence = None  # Pour stocker la dernière séquence traitée
    data_buffer = ""

    while True:
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            print("Attempting to connect to MAME server on port 8000...")
            sock.connect(server_address)
            print("Connected to MAME server.")

            commands = ['send_id = 0\1', 'send_id = 1\1', 'send_id = 2\1']
            for command in commands:
                print(f"Sending command: {command}")
                sock.sendall(command.encode('utf-8'))

            while True:
                data = sock.recv(1024)
                if data:
                    raw_data = data
                    # Récupère tous les fragments disponibles
                    while True:
                        ready, _, _ = select.select([sock], [], [], 0.01)
                        if ready:
                            more = sock.recv(1024)
                            if not more:
                                break
                            raw_data += more
                        else:
                            break

                    print("Raw data received:", repr(raw_data))
                    try:
                        decoded_data = raw_data.decode('utf-8')
                    except UnicodeDecodeError as e:
                        print("Error decoding data:", e)
                        continue

                    data_buffer += decoded_data
                    # On découpe le buffer sur '\r'
                    parts = data_buffer.split('\r')
                    # Tous les éléments sauf le dernier sont complets
                    complete_messages = [m.strip() for m in parts[:-1] if m.strip()]
                    # On garde le dernier fragment dans le buffer
                    data_buffer = parts[-1]

                    if complete_messages:
                        # Nettoyer les répétitions de séquences : extraire le bloc minimal
                        unique_block = find_repeating_block(complete_messages)
                        current_sequence = "\r".join(unique_block)
                        # Si cette séquence est identique à la précédente, on ne traite rien
                        if last_sequence is not None and current_sequence == last_sequence:
                            print("La séquence répétée est identique à la précédente, on l'ignore.")
                        else:
                            last_sequence = current_sequence
                            print("Séquence unique à traiter :")
                            print(current_sequence)
                            for message in unique_block:
                                process_message(message, last_states)
                else:
                    print("Connection closed by MAME.")
                    # process_message("mame_stop", {})
                    break

        except socket.error as e:
            print(f"Socket error: {e}")
        finally:
            sock.close()
            # process_message("mame_stop", {})
            print("Disconnected from MAME server.")

        print(f"Waiting {reconnection_delay} seconds before reconnecting...")
        time.sleep(reconnection_delay)

if __name__ == "__main__":
    main()