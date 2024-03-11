import sys
import os
import requests
import urllib.parse
import configparser
import subprocess
import shlex
import logging
import psutil
import threading
import time

#logging.basicConfig(level=logging.INFO)
logging.getLogger().addHandler(logging.NullHandler())

def load_config():
    try:
        base_path = os.path.join(os.getcwd(), *[os.pardir] * 4)
        config_ini_file_path = os.path.join(base_path, 'plugins', 'MarqueeManager', 'config.ini')

        if not os.path.exists(config_ini_file_path):
            logging.error("Fichier de configuration non trouvé.")
            sys.exit(1)

        config = configparser.ConfigParser()
        config.read(config_ini_file_path)

        logging.info(f"base_path: {base_path}")
        logging.info(f"config_ini_file_path: {config_ini_file_path}")

        return config
    except FileNotFoundError:
        logging.error("Fichier de configuration non trouvé.")
        sys.exit(1)

def send_event(event, params, server_url, current_time):
    try:
        encoded_params = {key: urllib.parse.quote_plus(str(value)) for key, value in params.items()}
        encoded_params['timestamp'] = current_time
        #encoded_params = {key: urllib.parse.quote_plus(value) for key, value in params.items()}
        response = requests.get(f"{server_url}", params={'event': event, **encoded_params})
        #print(response.text)
    except requests.exceptions.RequestException as e:
        logging.info(f"Erreur lors de l'envoi de l'événement : {e}")
        #input("Appuyez sur Entrée pour continuer...")

def get_current_directory_event():
    return os.path.basename(os.getcwd())

def get_command_line():
    pid = os.getpid()
    command = f'wmic process where ProcessId={pid} get CommandLine'
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, shell=True)
    output, error = process.communicate()
    if error:
        logging.info(f"Error: {error.decode('cp1252').strip()}")
        #input("Appuyez sur Entrée pour continuer...")
        return ""
    try:
        return output.decode('utf-8').strip()
    except UnicodeDecodeError:
        return output.decode('cp1252').strip()

def clean_and_split_arguments(command_line):
    command_line = command_line.replace('""', '"')
    try:
        arguments = shlex.split(command_line)
    except ValueError as e:
        logging.info(f"Erreur lors du découpage des arguments: {e}")
        arguments = []
    if arguments:
        arguments = arguments[2:]
    return arguments

if __name__ == "__main__":
    current_time = time.time()
    config = load_config()
    server_url = f"http://{config['Settings']['host']}:{config['Settings']['port']}"
    event = get_current_directory_event()
    command_line = get_command_line()
    #logging.info(f"command_line: {command_line}")
    arguments = clean_and_split_arguments(command_line)
    params = {f'param{i}': arg for i, arg in enumerate(arguments, start=1)}
    send_event(event, params, server_url, current_time)
