import pyautogui
import time

# Délai avant d'exécuter l'action pour s'assurer que l'application est lancée
time.sleep(10)

# Obtenir les dimensions de l'écran
screenWidth, screenHeight = pyautogui.size()

# Positionner le clic au milieu horizontalement et à 15 pixels du haut
pyautogui.click(screenWidth / 2, 15)

# Simuler un petit scroll
pyautogui.scroll(10)

# Le script se termine ici et se ferme de lui-même
