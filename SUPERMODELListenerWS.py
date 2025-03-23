#!/usr/bin/env python
import ctypes
from ctypes import wintypes
import win32gui, win32con
import configparser
import subprocess
import os
import logging
import re
import time

# --- Définition des types Windows pour ctypes ---
if hasattr(wintypes, 'ULONG_PTR'):
    ULONG_PTR = wintypes.ULONG_PTR
else:
    if ctypes.sizeof(ctypes.c_void_p) == 8:
        ULONG_PTR = ctypes.c_uint64
    else:
        ULONG_PTR = ctypes.c_uint32

class COPYDATASTRUCT(ctypes.Structure):
    _fields_ = [
        ("dwData", ULONG_PTR),
        ("cbData", wintypes.DWORD),
        ("lpData", ctypes.c_void_p)
    ]

def get_copydata_struct(lparam):
    cds = COPYDATASTRUCT.from_address(lparam)
    buffer = (ctypes.c_char * cds.cbData).from_address(cds.lpData)
    return cds, bytes(buffer)

# --- Configuration et fonction d'envoi vers MPV ---
config = configparser.ConfigParser()

def load_config():
    global config
    config.read('config.ini')
    if config['Settings'].get("logFile", "false").lower() == "true":
        logging.basicConfig(filename="SUPERMODELListener.log", level=logging.INFO)
        logging.info("Start logging")
    else:
        logging.basicConfig(level=logging.INFO)
    cwd = os.getcwd()
    def update_path(setting, default_path):
        if setting not in config['Settings'] or not os.path.isabs(config['Settings'].get(setting, '')):
            config['Settings'][setting] = default_path
    update_path('RetroBatPath', os.path.dirname(os.path.dirname(cwd)))
    if 'IPCChannel' not in config['Settings']:
        config['Settings']['IPCChannel'] = "mpv_ipc"
    logging.info("Configuration loaded.")

def recursive_clean_slashes(s):
    new_s = re.sub(r'\\+', r'\\\\', s)
    return new_s if new_s == s else recursive_clean_slashes(new_s)

def push_datas_to_MPV(action, datas):
    """
    Construit une chaîne de données au format "action|valeur1|valeur2|..."
    et exécute la commande associée (définie dans config.ini).
    Exemple de commande dans config.ini :
        [Commands]
        marquee-mame = echo {{"command":["script-message","mame-action","marquee-mame|{data}"]}}>\\.\pipe\mpv-pipe
        Commande push_datas_to_MPV exécutée avec succès : echo {"command":["script-message","mame-action","marquee-mame|TORP_LAMP_2 = 1"]}>\\.\pipe\mpv-pipe
    """
    if isinstance(datas, dict):
        data_str = "|".join([action] + [recursive_clean_slashes(str(value)) for value in datas.values()])
    elif isinstance(datas, (list, tuple)):
        data_str = "|".join([action] + [recursive_clean_slashes(str(item)) for item in datas])
    else:
        data_str = f"{action}|{str(datas)}"

    command_template = config['Commands'].get(action, "")
    if command_template:
        command = command_template.replace("{IPCChannel}", config['Settings']['IPCChannel'])
        command = command.replace("{data}", data_str)
        try:
            subprocess.run(command, shell=True, check=True)
            logging.info(f"Commande push_datas_to_MPV exécutée: {command}")
            print(f"Commande push_datas_to_MPV exécutée avec succès : {command}")
            return True
        except Exception as e:
            logging.error(f"Erreur lors de l'exécution de push_datas_to_MPV: {e}")
            return False
    else:
        logging.error(f"La commande pour action '{action}' n'est pas définie dans la configuration.")
        return False

# --- Gestion des mises à jour ---
last_states = {}      # Pour éviter d'envoyer un état inchangé
output_names = {}     # mapping: output_id -> nom (ex. 0 -> nom du jeu, 1 -> "pause", etc.)

def process_update(output_id, value):
    """
    Formate et envoie à MPV le message "nom = valeur" pour un output.
    Si le nom n'est pas encore connu, on utilise temporairement "ID<output_id>".
    Si l'output correspond à l'ID 0, on envoie "mame_start = <nom_du_jeu>".
    """
    if output_id == 0:
        # On vérifie que le nom est connu
        if 0 in output_names:
            game_name = output_names[0]
            message_str = f"mame_start = {game_name}"
            print(f"[Output] {message_str}")
            push_datas_to_MPV("marquee-mame", message_str)
        else:
            # Si le nom n'est pas encore reçu, on le demande
            if supermodel_hwnd:
                win32gui.PostMessage(supermodel_hwnd, WM_MAME_GETIDSTR, hwnd, 0)
        return

    # Pour les autres outputs, on utilise le nom s'il est disponible ou "ID<output_id>"
    name = output_names.get(output_id, f"ID{output_id}")
    message_str = f"{name} = {value}"
    print(f"[Output] {message_str}")
    global last_states
    match = re.match(r'\s*(\w+)\s*=\s*(\d+)\s*', message_str)
    if match:
        key = match.group(1)
        val_str = match.group(2)
        if key in last_states and last_states[key] == val_str:
            print(f"État inchangé pour {key}: {val_str}, pas d'envoi.")
            return
        last_states[key] = val_str
    push_datas_to_MPV("marquee-mame", message_str)
    # En option : si nécessaire, envoyer d'autres données (par exemple dimensions) lors d'un mame_start

# --- Écoute des messages Windows de Supermodel ---
WM_MAME_START       = win32gui.RegisterWindowMessage("MAMEOutputStart")
WM_MAME_STOP        = win32gui.RegisterWindowMessage("MAMEOutputStop")
WM_MAME_UPDATE      = win32gui.RegisterWindowMessage("MAMEOutputUpdateState")
WM_MAME_REGISTER    = win32gui.RegisterWindowMessage("MAMEOutputRegister")
WM_MAME_UNREGISTER  = win32gui.RegisterWindowMessage("MAMEOutputUnregister")
WM_MAME_GETIDSTR    = win32gui.RegisterWindowMessage("MAMEOutputGetIDString")

CLIENT_ID = 0x1234
supermodel_hwnd = None

def wndproc(hwnd, msg, wparam, lparam):
    global supermodel_hwnd, output_names
    if msg == WM_MAME_START:
        supermodel_hwnd = wparam
        print(f"[Info] Supermodel started, output window hWnd=0x{supermodel_hwnd:X}")
        # Demander le nom du jeu (ID 0)
        if supermodel_hwnd:
            win32gui.PostMessage(supermodel_hwnd, WM_MAME_GETIDSTR, hwnd, 0)
        win32gui.PostMessage(supermodel_hwnd, WM_MAME_REGISTER, hwnd, CLIENT_ID)
        return 0

    if msg == WM_MAME_STOP:
        print("[Info] Supermodel stopped.")
        push_datas_to_MPV("marquee-mame", "mame_stop")
        return 0

    if msg == WM_MAME_UPDATE:
        # wParam contient l'ID, lParam la valeur
        output_id = wparam
        value = lparam
        # Si le nom n'est pas connu, le demander
        if output_id not in output_names and supermodel_hwnd:
            win32gui.PostMessage(supermodel_hwnd, WM_MAME_GETIDSTR, hwnd, output_id)
        process_update(output_id, value)
        return 0

    if msg == win32con.WM_COPYDATA:
        try:
            cds, data_bytes = get_copydata_struct(lparam)
            if cds.dwData == 1:
                if len(data_bytes) >= 4:
                    out_id = int.from_bytes(data_bytes[:4], byteorder='little')
                    out_name = data_bytes[4:].split(b'\x00', 1)[0].decode('ascii', errors='ignore')
                    output_names[out_id] = out_name
                    print(f"[Info] Received name: ID{out_id} = \"{out_name}\"")
                    # Si c'est l'ID 0 (nom du jeu), envoyer mame_start avec ce nom
                    if out_id == 0:
                        process_update(0, 0)
        except Exception as e:
            print(f"[Erreur] Traitement de WM_COPYDATA: {e}")
        return 0

    if msg == win32con.WM_DESTROY:
        win32gui.PostQuitMessage(0)
        return 0

    return win32gui.DefWindowProc(hwnd, msg, wparam, lparam)

def main():
    load_config()
    # Création de la fenêtre client invisible
    wndclass = win32gui.WNDCLASS()
    wndclass.lpfnWndProc = wndproc
    wndclass.lpszClassName = "MyMameOutputClientWindow"
    class_atom = win32gui.RegisterClass(wndclass)
    client_hwnd = win32gui.CreateWindow(class_atom, "MAMEOutputClient", 0, 0, 0, 0, 0, 0, 0, 0, None)
    
    try:
        target_hwnd = win32gui.FindWindow("MAMEOutput", None)
    except Exception:
        target_hwnd = None

    if target_hwnd:
        supermodel_hwnd = target_hwnd
        print(f"[Info] Found MAMEOutput window: hWnd=0x{supermodel_hwnd:X}")
        win32gui.PostMessage(supermodel_hwnd, WM_MAME_REGISTER, client_hwnd, CLIENT_ID)
        # Demander les noms pour les IDs 0 à 14
        for i in range(0, 15):
            win32gui.PostMessage(supermodel_hwnd, WM_MAME_GETIDSTR, client_hwnd, i)
    else:
        print("[Info] MAMEOutput window not found. Waiting for MAMEOutputStart...")
    
    print("[Info] Listening for Windows messages...")
    win32gui.PumpMessages()

if __name__ == "__main__":
    main()
