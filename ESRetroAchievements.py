import configparser
import xml.etree.ElementTree as ET
import os
import time
import requests
import logging
import re
import json
import subprocess
import base64

logging.basicConfig(level=logging.INFO)

# Charger la configuration globale
config = configparser.ConfigParser()
def load_config():
    global config
    config.read('config.ini')
    current_working_dir = os.getcwd()  # C:\RetroBat\plugins\MarqueeManager\
    logging.info(f"{current_working_dir}")
    def update_path(setting, default_path):
        logging.info(f"update_path {setting} {default_path}")
        # Vérifier si la variable n'existe pas ou n'est pas un lien absolu
        if setting not in config['Settings'] or not os.path.isabs(config['Settings'].get(setting, '')):
            config['Settings'][setting] = default_path

    update_path('RetroBatPath', os.path.dirname(os.path.dirname(current_working_dir)))
    update_path('RomsPath', os.path.join(config['Settings']['RetroBatPath'], 'roms'))
    update_path('DefaultImagePath', os.path.join(current_working_dir, 'images', 'default.png'))
    update_path('MarqueeImagePath', os.path.join(current_working_dir, 'images'))
    update_path('MarqueeImagePathDefault', os.path.join(config['Settings']['RetroBatPath'], 'roms'))
    update_path('SystemMarqueePath', os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'themes', 'es-theme-carbon-master', 'art', 'logos'))
    update_path('CollectionMarqueePath', os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'themes', 'es-theme-carbon-master', 'art', 'logos'))
    update_path('MPVPath', os.path.join(current_working_dir, 'mpv', 'mpv.exe'))
    update_path('IMPath', os.path.join(current_working_dir, 'imagemagick', 'convert.exe'))

# Déclaration de la variable globale
es_settings = None
current_game_id = None
current_game_info = None
current_player = None
current_lbname = None
user_hardcore_mode = False
login_attempt_start_time = None

# Chemins des dossiers de cache
RA_BASE_CACHE = 'RA'
RA_BADGES_CACHE = os.path.join(RA_BASE_CACHE, 'Badge')
RA_IMAGES_CACHE = os.path.join(RA_BASE_CACHE, 'Images')
RA_API_CACHE = os.path.join(RA_BASE_CACHE, 'API')
RA_USERPICS_CACHE = os.path.join(RA_BASE_CACHE, 'UserPic')
RA_CACHE = os.path.join(RA_BASE_CACHE, 'Cache')

# Créer les dossiers de cache si nécessaire
os.makedirs(RA_BADGES_CACHE, exist_ok=True)
os.makedirs(RA_IMAGES_CACHE, exist_ok=True)
os.makedirs(RA_API_CACHE, exist_ok=True)
os.makedirs(RA_USERPICS_CACHE, exist_ok=True)
os.makedirs(RA_CACHE, exist_ok=True)

# Fonction pour sauvegarder une image
def save_image(url, path):
    if not os.path.exists(path):  # Télécharger l'image seulement si elle n'existe pas déjà
        response = requests.get(url)
        if response.status_code == 200:
            with open(path, 'wb') as file:
                file.write(response.content)
# Charger les configurations depuis es_settings.cfg

def load_es_settings(xml_relative_path):
    script_dir = os.path.dirname(os.path.realpath(__file__))
    xml_path = os.path.join(script_dir, xml_relative_path)
    tree = ET.parse(xml_path)
    root = tree.getroot()

    es_settings = {}

    # Ajouter les valeurs de type string
    for setting in root.findall('string'):
        key = setting.get('name')
        value = setting.get('value')
        es_settings[key] = value

    # Ajouter les valeurs de type bool
    for setting in root.findall('bool'):
        key = setting.get('name')
        value = setting.get('value').lower() == 'true'
        es_settings[key] = value

    # Ajouter les valeurs de type int
    for setting in root.findall('int'):
        key = setting.get('name')
        value = int(setting.get('value'))
        es_settings[key] = value

    return es_settings


# Variables d'identification pour l'API
API_USERNAME = ''  # z
API_TOKEN = ''  # y

def clear_retroarch_log(config):
    log_path = os.path.join(config['Settings']['RetroBatPath'], 'emulators', 'retroarch', 'logs', 'retroarch.log')
    if os.path.exists(log_path):
        open(log_path, 'w').close()  # Ouvrir en mode écriture et fermer immédiatement pour vider le fichier
        logging.info("Le fichier retroarch.log a été vidé")
    else:
        logging.info("Le fichier retroarch.log n'existe pas encore")

# Modifier retroarch.cfg pour activer la journalisation
def modify_retroarch_config(config):
    retroarch_config_path = os.path.join(config['Settings']['RetroBatPath'], 'emulators', 'retroarch', 'retroarch.cfg')
    with open(retroarch_config_path, 'a') as file:
        file.write("\nlog_to_file = \"true\"\n")
        file.write("log_to_file_timestamp = \"false\"\n")
        file.write("log_verbosity = \"true\"\n")
    logging.info("Configuration de RetroArch modifiée pour activer la journalisation")

# Fonction générique pour appeler l'API RetroAchievements
def call_retroachievements_api(endpoint, params):
    base_url = "https://retroachievements.org/API"
    full_url = f"{base_url}/{endpoint}"

    # Ajout des identifiants de l'API
    params['z'] = API_USERNAME
    params['y'] = API_TOKEN

    try:
        response = requests.get(full_url, params=params)
        response.raise_for_status()
        logging.info(f"Appel API réussi : {endpoint}")
        return response.json()
    except requests.exceptions.RequestException as e:
        logging.error(f"Erreur lors de l'appel à l'API : {e}")
        return None

# Fonction pour sauvegarder une image
def save_image(image_url, image_path):
    if not os.path.exists(image_path):  # Vérifier si l'image n'existe pas déjà
        try:
            logging.info(f"Téléchargement de l'image depuis {image_url}")
            response = requests.get(image_url)
            if response.status_code == 200:
                with open(image_path, 'wb') as file:
                    file.write(response.content)
                logging.info(f"Image enregistrée avec succès : {image_path}")
            else:
                logging.error(f"Échec du téléchargement de l'image. Code de statut : {response.status_code}")
        except Exception as e:
            logging.error(f"Erreur lors du téléchargement de l'image : {e}")
    else:
        logging.info(f"L'image existe déjà : {image_path}")

# Fonction pour sauvegarder la réponse de l'API dans un fichier JSON
def save_api_response(filename, data):
    path = os.path.join(RA_API_CACHE, filename)
    with open(path, 'w') as file:
        json.dump(data, file)
    logging.info(f"Réponse API enregistrée dans : {path}")

# Fonction pour charger une réponse de l'API à partir du cache
def load_api_response(filename):
    path = os.path.join(RA_API_CACHE, filename)
    if os.path.exists(path):
        with open(path, 'r') as file:
            logging.info(f"Chargement de la cache API de : {path}")
            return json.load(file)
    return None

# Fonction pour récupérer les informations de profil de l'utilisateur
def get_user_profile(player_username):
    # Vérifiez d'abord si une réponse en cache est disponible
    global current_player
    current_player = player_username
    cached_response = load_api_response(f"API_GetUserProfile.php_user{player_username}.json")
    if cached_response:
        logging.info(f"Chargement de la réponse API en cache pour l'utilisateur {player_username}.")
        return cached_response
    # Si aucune réponse en cache, faites un appel API
    response = call_retroachievements_api("API_GetUserProfile.php", {'u': player_username})
    if response:
        # Mettre en cache l'image de l'utilisateur
        user_pic_name = response.get('UserPic')
        if user_pic_name:
            user_pic_url = f"https://media.retroachievements.org{user_pic_name}"
            user_pic_path = os.path.join(RA_USERPICS_CACHE, os.path.basename(user_pic_name))
            save_image(user_pic_url, user_pic_path)
            response['UserPic'] = user_pic_path
        logging.info(f"User profile data received for {player_username}.")
        # Sauvegarder la réponse de l'API dans le cache
        filename = f"API_GetUserProfile.php_user{player_username}.json"
        save_api_response(filename, response)
        return response
    else:
        logging.error(f"Failed to fetch user profile for {player_username}.")
        return None

def get_game_info(game_id, player_username):
    # Vérifiez d'abord si une réponse en cache est disponible
    cached_response = load_api_response(f"API_GetGame.php_game{game_id}.json")
    if cached_response:
        logging.info(f"Chargement de la réponse API en cache pour le jeu ID {game_id}.")
        return cached_response

    # Si aucune réponse en cache, faites un appel API
    params = {'i': game_id, 'u': player_username}
    response = call_retroachievements_api("API_GetGame.php", params)
    if response:
        logging.info(f"API response received for game ID {game_id}.")
        for image_key in ['GameIcon', 'ImageIcon', 'ImageTitle', 'ImageIngame', 'ImageBoxArt']:
            image_name = response.get(image_key)
            if image_name:
                image_url = f"https://media.retroachievements.org{image_name}"
                image_path = os.path.join(RA_IMAGES_CACHE, os.path.basename(image_name))
                save_image(image_url, image_path)
                local_image_path = os.path.join('RA', 'Images', os.path.basename(image_name))
                response[image_key] = local_image_path  # Enregistrez le chemin local de l'image
                #logging.info(f"Image : {image_key} -> {image_path}")
        # Sauvegardez la réponse formatée dans le cache
        filename = f"API_GetGame.php_game{game_id}.json"
        save_api_response(filename, response)
    return response


def get_user_progress(game_id, player_username):
    # Pour API_GetGameInfoAndUserProgress.php, toujours faire un appel API frais
    params = {'g': game_id, 'u': player_username}
    response = call_retroachievements_api("API_GetGameInfoAndUserProgress.php", params)

    # Vérifier si la réponse est valide et sans erreur
    if response and not response.get('Error'):
        logging.info(f"API response received for user progress in game ID {game_id}.")
        for image_key in ['ImageIcon', 'ImageTitle', 'ImageIngame', 'ImageBoxArt']:
            image_name = response.get(image_key)
            if image_name:
                image_url = f"https://media.retroachievements.org{image_name}"
                image_path = os.path.join(RA_IMAGES_CACHE, os.path.basename(image_name))
                save_image(image_url, image_path)
                local_image_path = os.path.join('RA', 'Images', os.path.basename(image_name))
                response[image_key] = local_image_path  # Enregistrez le chemin local de l'image

        for ach_id, ach_info in response.get('Achievements', {}).items():
            badge_name = ach_info.get('BadgeName')
            if badge_name:
                badge_url = f"https://media.retroachievements.org/Badge/{badge_name}.png"
                badge_path = os.path.join(RA_BADGES_CACHE, f"{badge_name}.png")
                save_image(badge_url, badge_path)
                local_badge_path = os.path.join('RA', 'Badge', badge_name + '.png')
                ach_info['BadgeName'] = local_badge_path  # Enregistrez le chemin local du badge

        # Sauvegarder la réponse formatée dans le cache
        filename = f"API_GetGameInfoAndUserProgress.php_game{game_id}.json"
        save_api_response(filename, response)
    else:
        # Si l'API échoue, charger les données à partir du cache
        logging.info(f"API call failed or returned error, loading cached data for game ID {game_id}.")
        response = load_api_response(f"API_GetGameInfoAndUserProgress.php_game{game_id}.json")

    return response

def convert_time_to_seconds(time_str):
    """ Convertit une chaîne de temps au format 'm:ss.ss' en secondes. """
    if time_str == 'N/A' or time_str == 'No Record':
        return float('inf')  # Retourne l'infini pour les temps non définis
    minutes, seconds = map(float, time_str.split(':'))
    return minutes * 60 + seconds

def get_user_leaderboard(lbid, lbname=None, lbtime=None):
    response = {}
    response['lbid'] = lbid
    response['lbname'] = lbname if lbname is not None else 'Unknown'
    response['lbtime'] = lbtime if lbtime is not None else 'N/A'
    # Récupération du meilleur temps pour le leaderboard spécifique
    response['lbbest_time'] = leaderboard_times.get(lbid, 'No Record')

    # Convertir les temps en secondes pour comparaison
    lbtime_seconds = convert_time_to_seconds(response['lbtime'])
    lbbest_time_seconds = convert_time_to_seconds(response['lbbest_time'])

    # Vérifier si le nouveau temps est un record
    response['recordbeaten'] = lbtime_seconds < lbbest_time_seconds

    return response

# Surveiller retroarch.log pour les succès
def watch_retroarch_log(config, last_line_num, player_username):
    log_path = os.path.join(config['Settings']['RetroBatPath'], 'emulators', 'retroarch', 'logs', 'retroarch.log')
    with open(log_path, 'a+') as file:
            pass

    with open(log_path, 'r') as file:
        lines = file.readlines()
        new_line_num = len(lines)

        # Nouveau jeu détecté si le nombre de lignes est inférieur au dernier nombre enregistré
        if new_line_num < last_line_num:
            logging.info("Nouveau jeu détecté")
            last_line_num = 0

        # Traiter toutes les nouvelles lignes
        if new_line_num > last_line_num:
            for line in lines[last_line_num:new_line_num]:
                process_log_line(line, player_username)
            last_line_num = new_line_num

        return last_line_num


    # [INFO] [RCHEEVOS]: Load started, hardcore active
    # [INFO] [RCHEEVOS]: Using host: https://retroachievements.org
    # [INFO] [RCHEEVOS]: Attempting to login Nelfe (with password)
    # [INFO] [RCHEEVOS]: Nelfe logged in successfully
    # [INFO] [RCHEEVOS]: Fetched game data 1
    # [INFO] [RCHEEVOS]: Starting periodic rich presence update task for game 1
    # [INFO] [RCHEEVOS]: Registered 0x10000 bytes of SYSTEM RAM at $000000 (offset 0x000000)
    # [INFO] [RCHEEVOS]: Registered 0x10000 bytes of SRAM at $010000 (offset 0x000000)
    # [INFO] [RCHEEVOS]: Load finished
    # [INFO] [RCHEEVOS]: All 24 achievements activated for this session.
    # [INFO] [RCHEEVOS]: Started session for game 1
    # [INFO] [RCHEEVOS]: Awarding achievement 2: Amateur Collector (Collect 20 rings)
    # [INFO] [RCHEEVOS]: Achievement 2: User already has this achievement unlocked in hardcore mode.
    # [INFO] [RCHEEVOS]: Awarded achievement 2
    # [INFO] [RCHEEVOS]: Leaderboard 4 started: Green Hill Zone - Act 3
    # [INFO] [RCHEEVOS]: Leaderboard 4 canceled: Green Hill Zone - Act 3
    # [INFO] [RCHEEVOS]: Submitting 4:57.55 for leaderboard 4
    # [INFO] [RCHEEVOS]: Submitted leaderboard 4
    # [INFO] [RCHEEVOS]: Error logging in: HTTP error code 504
    # [INFO] [Core]: Unloading game..

def process_log_line(line, player_username):
    global current_game_id  # Utilisation de la variable globale
    global current_game_info  # Utilisation de la variable globale

    # Détection de la connection d'un joueur
    user_login_match = re.search(r"RCHEEVOS\]: (.+) logged in successfully", line)
    if user_login_match:
        logged_user = user_login_match.group(1)
        logging.info(f"Utilisateur connecté : {logged_user}")
        login_attempt_start_time = time.time()
        user_profile = get_user_profile(logged_user)
        handle_user_info(user_profile)

    # Détection du chargement d'un jeu
    game_data_match = re.search(r"Fetched game data (\d+)", line)
    if game_data_match:
        current_game_id = game_data_match.group(1)
        logging.info(f"Nouveau jeu chargé: ID {current_game_id}")
        game_info = get_game_info(current_game_id, player_username)
        current_game_info = game_info
        user_progress = get_user_progress(current_game_id, player_username)
        # Traitement des informations du jeu et de l'utilisateur
        handle_game_info(game_info, user_progress)

    # Détection de l'arrêt d'un jeu
    game_stop_match = re.search(r"\[INFO\] \[Core\]: Unloading game\.\.", line)
    if game_stop_match:
       logging.info("Jeu déchargé.")
       handle_game_stop()

    # Détection d'un succès décerné
    achievement_match = re.search(r"Awarded achievement (\d+)", line)
    if achievement_match and current_game_id:
        achievement_id = achievement_match.group(1)
        logging.info(f"Succès décerné: ID {achievement_id}")
        # Appeler l'API pour obtenir les détails du succès
        user_progress = get_user_progress(current_game_id, player_username)
        # Traitement de l'information du succès
        handle_achievement_info(user_progress, achievement_id, current_game_info)

    global current_lbname
    # Détection d'un challenge leaderboard
    lb_started_match = re.search(r"Leaderboard (\d+) started: (.+)", line)
    if lb_started_match:
        lbid, lbname = lb_started_match.groups()
        current_lbname = lbname
        user_leaderboard = get_user_leaderboard(lbid, lbname)
        handle_leaderboard_started(user_leaderboard)

    # Détection d'un challenge leaderboard annulé
    lb_canceled_match = re.search(r"Leaderboard (\d+) canceled: (.+)", line)
    if lb_canceled_match:
        lbid, lbname = lb_canceled_match.groups()
        handle_leaderboard_canceled(lbid, lbname)

    # Détection d'un challenge leaderboard soumis
    lb_submitting_match = re.search(r"Submitting (\d+:\d+\.\d+) for leaderboard (\d+)", line)
    if lb_submitting_match:
        lbtime, lbid = lb_submitting_match.group(1), lb_submitting_match.group(2)
        user_leaderboard = get_user_leaderboard(lbid, current_lbname, lbtime)
        handle_leaderboard_submitting(user_leaderboard)

    # Détection d'un challenge leaderboard approuvé
    lb_submitted_match = re.search(r"Submitted leaderboard (\d+)", line)
    if lb_submitted_match:
        lbid = lb_submitted_match.group(1)
        handle_leaderboard_submitted(lbid)

def handle_user_info(user_profile):
    global user_hardcore_mode
    # Extraction des informations de l'utilisateur
    user_name = user_profile.get('User', 'Unknown User')
    user_pic_name = user_profile.get('UserPic', '')
    user_pic_url = os.path.join(config['Settings']['RetroBatPath'], 'plugins', 'MarqueeManager', user_pic_name).replace("\\", "\\\\")
    es_settings_path = os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'es_settings.cfg')
    es_settings = load_es_settings(es_settings_path)
    user_hardcore_mode = es_settings.get('global.retroachievements.hardcore', False)
    logging.info(f"#user_hardcore_mode: {user_hardcore_mode}")
    # Préparation des données pour l'envoi à MPV
    user_data = {
        'user_name': user_name,
        'user_pic': user_pic_url,
        'user_language' : config['Settings']['Language'],
        'hardcode_mode' : user_hardcore_mode
    }

    # Appel de la fonction pushRAdatasToMPV
    pushRAdatasToMPV("user_info", user_data)

def handle_game_stop():
    global current_game_id  # Assurez-vous que current_game_id est accessible globalement
    # Préparation des données pour l'envoi à MPV
    game_data = {
        'current_game_id': current_game_id
    }
    # Réinitialiser current_game_id à None ou à une valeur par défaut après la gestion de l'arrêt du jeu
    current_game_id = None
    # Appel de la fonction pushRAdatasToMPV
    pushRAdatasToMPV("game_stop", game_data)

def handle_game_info(game_info, user_progress):
    global es_settings
    global user_hardcore_mode
    # Extraction des informations du jeu
    game_title = game_info.get('Title', 'Unknown Game')
    game_icon_name = game_info.get('GameIcon', '')
    game_icon_url = os.path.join(config['Settings']['RetroBatPath'], 'plugins', 'MarqueeManager', game_icon_name).replace("\\", "\\\\")

    logging.info(f"#config['Settings']['RetroBatPath']: {config['Settings']['RetroBatPath']}")
    logging.info(f"#game_icon_name: {game_icon_name}")
    logging.info(f"#game_icon_url: {game_icon_url}")

    # Extraction des informations de progression de l'utilisateur
    num_awarded_to_user = user_progress.get('NumAwardedToUser', 0)
    user_completion = user_progress.get('UserCompletion', '0%')
    total_achievements = len(user_progress.get('Achievements', {}))
    total_points = 0
    logging.info(f"Hardcore Mode : {user_hardcore_mode}")
    # Création et tri des données des succès
    achievements_data = []
    for ach_id, ach_info in user_progress.get('Achievements', {}).items():
        unlock = 'DateEarnedHardcore' in ach_info if user_hardcore_mode else 'DateEarned' in ach_info
        points = ach_info.get('Points', 0)
        total_points += points if unlock else 0

        achievement_data = {
            'ID': ach_info.get('ID'),
            'NumAwarded': ach_info.get('NumAwarded'),
            'NumAwardedHardcore': ach_info.get('NumAwardedHardcore'),
            'Title': ach_info.get('Title'),
            'Description': ach_info.get('Description'),
            'Points': points,
            'TrueRatio': ach_info.get('TrueRatio'),
            'BadgeURL': ach_info.get('BadgeName').replace("\\", "\\\\"),
            'DisplayOrder': ach_info.get('DisplayOrder'),
            'Type': ach_info.get('type'),
            'Unlock': unlock
        }
        achievements_data.append(achievement_data)

    # Trier les succès par DisplayOrder
    achievements_data.sort(key=lambda x: x['DisplayOrder'])


    # Affichage des informations générales
    logging.info(f"Game Title: {game_title}")
    logging.info(f"Game Icon: {game_icon_url}")
    logging.info(f"Number of Achievements Unlocked by User: {num_awarded_to_user} out of {total_achievements}")
    logging.info(f"User Completion Percentage: {user_completion}")
    logging.info(f"Total Points from Unlocked Achievements: {total_points}")

    # Préparation des données générales du jeu pour l'envoi à MPV
    game_data = {
        'game_title': game_title,
        'game_icon': game_icon_url,
        'num_awarded_to_user': num_awarded_to_user,
        'user_completion': user_completion,
        'total_achievements': total_achievements,
        'total_points': total_points
    }

    # Envoi des informations de chaque succès à MPV
    for achievement_data in achievements_data:
        pushRAdatasToMPV("achievement_info", achievement_data)
    # Envoi des informations de chaque leaderboard du jeu courant à MPV
    leaderboard_times = load_leaderboardtimes(current_game_id, current_player)
    for lbid, lbtime in leaderboard_times.items():
            leaderboard_data = {
                'lbid': lbid,
                'lbtime': lbtime
            }
            pushRAdatasToMPV("leaderboardtimes", leaderboard_data)
    # Appel de la fonction pushRAdatasToMPV pour les données générales du jeu, en dernier pour être sûr d'avoir en amont les achievements
    pushRAdatasToMPV("game_info", game_data)

def handle_achievement_info(user_progress, achievement_id, game_info):
    achievements = user_progress.get('Achievements', {})
    achievement = achievements.get(str(achievement_id), {})

    if achievement:
        title = achievement.get('Title', 'Unknown Title')
        description = achievement.get('Description', 'No description')
        points = achievement.get('Points', 0)
        badge_name = achievement.get('BadgeName', '')

        # Construction du chemin de l'image
        badge_url = os.path.join(config['Settings']['RetroBatPath'], 'plugins', 'MarqueeManager', badge_name).replace("\\", "\\\\")

        logging.info(f"Achievement ID: {achievement_id}")
        logging.info(f"Title: {title}")
        logging.info(f"Description: {description}")
        logging.info(f"Points: {points}")
        logging.info(f"Badge URL: {badge_url}")

    # Informations supplémentaires
    num_awarded_to_user = user_progress.get('NumAwardedToUser', 0)
    user_completion = user_progress.get('UserCompletion', '0%')
    logging.info(f"User Awarded: {num_awarded_to_user}, User Completion: {user_completion}")

    # Informations du jeu
    game_icon = game_info.get('GameIcon', '')
    logging.info(f"Game Icon URL: {game_icon}")

    # Préparation des données pour l'envoi
    achievement_data = {
        'game_id': achievement_id,
        'url_image': badge_url,
        'title': title,
        'content': description,
        'num_awarded_to_user': num_awarded_to_user,
        'user_completion': user_completion
    }

    # Appel de la nouvelle fonction pushRAdatasToMPV
    pushRAdatasToMPV("achievement", achievement_data)


def handle_leaderboard_started(user_leaderboard):
    # Affichage des informations de log
    logging.info(f"Leaderboard Started: ID {user_leaderboard.get('lbid')}, Name: {user_leaderboard.get('lbname')}, Best Time: {user_leaderboard.get('lbbest_time')}")

    # Préparation des données pour l'envoi à MPV
    leaderboard_data = {
        'lbid': user_leaderboard.get('lbid'),
        'lbname': user_leaderboard.get('lbname'),
        'best_time': user_leaderboard.get('lbbest_time')
    }
    # Appel de la fonction pushRAdatasToMPV
    pushRAdatasToMPV("leaderboard_event_started", leaderboard_data)

def handle_leaderboard_canceled(lbid, lbname):
    # Logique pour gérer un leaderboard annulé
    logging.info(f"Leaderboard {lbid} canceled: {lbname}")
    # Préparation des données pour l'envoi à MPV
    leaderboard_data = {
        'lbid': lbid,
        'lbname': lbname
    }
    # Appel de la fonction pushRAdatasToMPV
    pushRAdatasToMPV("leaderboard_event_canceled", leaderboard_data)

def handle_leaderboard_submitting(user_leaderboard):
    global current_game_id, current_player  # Utiliser les variables globales pour les IDs actuels

    lbid = user_leaderboard.get('lbid')
    lbtime = user_leaderboard.get('lbtime')
    recordbeaten = user_leaderboard.get('recordbeaten')

    # Mettre à jour le meilleur temps si le record est battu
    if recordbeaten:
        updated = update_leaderboardtime(current_game_id, current_player, lbid, lbtime)
        logging.info(f"Leaderboard {lbid} updated: {updated}")

    # Logique pour gérer un leaderboard terminée avec le chronomètre stoppé
    logging.info(f"Leaderboard {lbid} time: {lbtime}, record: {recordbeaten}")

    leaderboard_data = {
        'lbid': lbid,
        'lbtime': lbtime,
        'recordbeaten': recordbeaten
    }
    # Appel de la fonction pushRAdatasToMPV
    pushRAdatasToMPV("leaderboard_event_submitting", leaderboard_data)

def handle_leaderboard_submitted(lbid):
    # Logique pour gérer un leaderboard soumis
    logging.info(f"Submitted leaderboard {lbid}")
    # Préparation des données pour l'envoi à MPV
    leaderboard_data = {
            'lbid': lbid
        }
    # Appel de la fonction pushRAdatasToMPV
    pushRAdatasToMPV("leaderboard_event_submitted", leaderboard_data)

leaderboard_times = {}
def load_leaderboardtimes(game_id, player_username):
    global leaderboard_times
    leaderboard_directory = os.path.join('RA', 'Leaderboard')
    leaderboard_file = os.path.join(leaderboard_directory, f"{player_username}-leaderboardsLocal.ra")
    leaderboard_times = {}

    if os.path.exists(leaderboard_file):
        with open(leaderboard_file, 'r') as file:
            for line in file:
                parts = line.strip().split('|')
                if parts[0] == str(game_id):
                    lbid, lbtime = parts[1], parts[2]
                    leaderboard_times[lbid] = lbtime
                    logging.info(f"Leaderboard ID {lbid} - Best time: {lbtime}")  # Affiche les informations dans la console

    return leaderboard_times

def update_leaderboardtime(game_id, player_username, lbid, lbtime):
    leaderboard_directory = os.path.join('RA', 'Leaderboard')
    os.makedirs(leaderboard_directory, exist_ok=True)
    leaderboard_file = os.path.join(leaderboard_directory, f"{player_username}-leaderboardsLocal.ra")
    lines_to_keep = []

    if os.path.exists(leaderboard_file):
        with open(leaderboard_file, 'r') as file:
            lines = file.readlines()

        for line in lines:
            parts = line.strip().split('|')
            if not (parts[0] == str(game_id) and parts[1] == lbid):
                lines_to_keep.append(line)

    # Ajouter ou mettre à jour le temps du leaderboard
    lines_to_keep.append(f"{game_id}|{lbid}|{lbtime}\n")

    # Écrire les données mises à jour dans le fichier
    with open(leaderboard_file, 'w') as file:
        file.writelines(lines_to_keep)

def pushRAdatasToMPV(type, datas):
    # Joindre les données en une chaîne séparée par des barres verticales
    data_str = "|".join([type] + [str(value) for value in datas.values()])

    # Récupération de la commande depuis le fichier de configuration
    command_template = config['Settings']['MPVPushRetroAchievementsDatas']
    if command_template:
        # Remplacement du placeholder {data} par la chaîne
        command = command_template.replace("{IPCChannel}", config['Settings']['IPCChannel'])
        command = command.replace("{data}", data_str)

        # Exécution de la commande
        try:
            subprocess.run(command, shell=True, check=True)
            logging.info(f"Commande exécutée avec succès : {command}")
        except Exception as e:
            logging.error(f"Erreur lors de l'exécution de la commande : {e}")
    else:
        logging.error("La commande MPVPushRetroAchievementsDatas n'est pas définie dans le fichier de configuration.")

# Fonction principale
def main():
    clear_retroarch_log(config)
    global es_settings
    es_settings_path = os.path.join(config['Settings']['RetroBatPath'], 'emulationstation', '.emulationstation', 'es_settings.cfg')
    es_settings = load_es_settings(es_settings_path)

    player_username = es_settings['global.retroachievements.username']

    if config['Settings']['MarqueeRetroAchievements'] == "true":
        modify_retroarch_config(config)
        last_line_num = 0
        while True:
            last_line_num = watch_retroarch_log(config, last_line_num, player_username)
            time.sleep(1)

if __name__ == "__main__":
    load_config()
    main()
