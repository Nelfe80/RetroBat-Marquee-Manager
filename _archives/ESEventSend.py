import os
import requests
import urllib.parse
import subprocess
import shlex
import logging
import time

#logging.basicConfig(level=logging.INFO)
logging.getLogger().addHandler(logging.NullHandler())

def send_event(event, params, server_url, current_time):
    try:
        encoded_params = {key: urllib.parse.quote_plus(str(value)) for key, value in params.items()}
        encoded_params['timestamp'] = current_time
        response = requests.get(f"{server_url}", params={'event': event, **encoded_params})
    except requests.exceptions.RequestException as e:
        logging.info(f"Erreur lors de l'envoi de l'événement : {e}")

def get_command_line():
    pid = os.getpid()
    command = f'wmic process where ProcessId={pid} get CommandLine'
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, shell=True)
    output, error = process.communicate()
    if error:
        logging.info(f"Error: {error.decode('cp1252').strip()}")
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
    command_line = get_command_line()
    #logging.info(f"command_line: {command_line}")
    arguments = clean_and_split_arguments(command_line)
    params = {f'param{i}': arg for i, arg in enumerate(arguments, start=1)}
    send_event("game-select", params, f"http://127.0.0.1:8080", time.time())