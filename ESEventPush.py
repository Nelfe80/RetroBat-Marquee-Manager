import sys
import os
import requests
import urllib.parse
import configparser
import subprocess
import shlex

def find_ini_file(start_path):
    current_path = start_path
    while current_path != os.path.dirname(current_path):
        ini_path = os.path.join(current_path, 'marquees', 'events.ini')
        if os.path.exists(ini_path):
            return ini_path
        current_path = os.path.dirname(current_path)
    raise FileNotFoundError("Le fichier events.ini n'a pas été trouvé.")

def load_config():
    current_working_dir = os.getcwd()
    config_path = find_ini_file(current_working_dir)

    config = configparser.ConfigParser()
    config.read(config_path)
    return config

def send_event(event, params, server_url):
    try:
        encoded_params = {key: urllib.parse.quote_plus(value) for key, value in params.items()}
        response = requests.get(f"{server_url}", params={'event': event, **encoded_params})
        print(response.text)
    except requests.exceptions.RequestException as e:
        print(f"Erreur lors de l'envoi de l'événement : {e}")

def get_current_directory_event():
    return os.path.basename(os.getcwd())

def get_command_line():
    pid = os.getpid()
    command = f'wmic process where ProcessId={pid} get CommandLine'
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, shell=True)
    output, error = process.communicate()
    if error:
        print(f"Error: {error.decode().strip()}")
        return ""
    return output.decode().strip()

def clean_and_split_arguments(command_line):
    command_line = command_line.replace('""', '"')
    try:
        arguments = shlex.split(command_line)
    except ValueError as e:
        print(f"Erreur lors du découpage des arguments: {e}")
        arguments = []
    if arguments:
        arguments = arguments[2:]
    return arguments

if __name__ == "__main__":
    config = load_config()
    server_url = f"http://{config['Settings']['host']}:{config['Settings']['port']}"
    event = get_current_directory_event()
    command_line = get_command_line()
    arguments = clean_and_split_arguments(command_line)
    params = {f'param{i}': arg for i, arg in enumerate(arguments, start=1)}
    send_event(event, params, server_url)
