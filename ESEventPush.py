import sys
import os
import requests
import urllib.parse
import configparser

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

def clean_argument(arg):
    if arg.startswith('""') and arg.endswith('""'):
        return arg[1:-1]
    return arg.strip('"')

def extract_params(args):
    cleaned_args = [clean_argument(arg) for arg in args]
    return {f'param{i+1}': arg for i, arg in enumerate(cleaned_args)}

if __name__ == "__main__":
    config = load_config()
    server_url = f"http://{config['Settings']['host']}:{config['Settings']['port']}"
    event = get_current_directory_event()
    params = extract_params(sys.argv[1:])
    send_event(event, params, server_url)
