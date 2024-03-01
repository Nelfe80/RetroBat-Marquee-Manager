import requests
import json
import os
import logging
import time

def get_ra_data(api_url, params, retries=3, delay=1):
    for attempt in range(retries):
        try:
            response = requests.get(api_url, params=params)
            if response.status_code == 200:
                return response.json()
            else:
                logging.error("Échec de l'appel API RetroAchievements: " + response.text)
        except requests.RequestException as e:
            logging.error("Erreur lors de l'appel API RetroAchievements: " + str(e))

        if attempt < retries - 1:
            logging.info(f"Tentative {attempt + 1} échouée, nouvelle tentative dans {delay} secondes.")
            time.sleep(delay)

    return None

def save_data_to_file(data, file_name):
    with open(file_name, 'w') as file:
        json.dump(data, file, indent=4)
    logging.info(f"Les données ont été enregistrées dans {file_name}")

def main():
    logging.basicConfig(level=logging.INFO)

    # Paramètres de l'API
    username = 'Nelfe'
    token = 'WPg4ngbUsJdtF5Pk97OQ91Mle0FnGQqx'

    # Vérifier et traiter la liste des systèmes
    systems_file = 'RAsystems.json'
    if not os.path.exists(systems_file):
        systems_url = "https://retroachievements.org/API/API_GetConsoleIDs.php"
        systems_params = {'z': username, 'y': token}
        systems_data = get_ra_data(systems_url, systems_params)

        if systems_data:
            save_data_to_file(systems_data, systems_file)
    else:
        logging.info(f"Le fichier {systems_file} existe déjà. Aucune requête API nécessaire.")

    # Charger la liste des systèmes
    with open(systems_file, 'r') as file:
        systems_data = json.load(file)

    # Récupérer et sauvegarder la liste des jeux pour chaque système
    for system in systems_data:
        games_file = f'RAgamelist{system["ID"]}.json'
        if not os.path.exists(games_file):
            games_url = f"https://retroachievements.org/API/API_GetGameList.php?i={system['ID']}&f=1&h=1"
            games_params = {'z': username, 'y': token}
            games_data = get_ra_data(games_url, games_params)

            if games_data:
                save_data_to_file(games_data, games_file)
            time.sleep(1)  # Pause entre les requêtes
        else:
            logging.info(f"Le fichier {games_file} existe déjà. Aucune requête API nécessaire.")

if __name__ == "__main__":
    main()
