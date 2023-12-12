<p>Thanks to Aynshe et Retrobat's community testers. </p>

<h1>Dynamic Marquees for RetroBat</h1>

<p>This project enables **dynamic display of marquees** on a secondary screen for RetroBat users on Windows 8+, utilizing custom scripts to manage the display based on user interactions.
</p>

<h2>Components</h2>
<p>
**ESEvents.py**: A Flask script that listens for HTTP requests and updates the marquee image on the secondary screen.<br>
**ESEventPush.py**: Script to send event information (like game or system selection) to ESEvents.py.<br>
**events.ini**: Configuration file to set paths for marquees, accepted formats, and commands to interact with MPV (media player).<br>
**RenameMarquees.bat**: Batch script for renaming marquees to prevent them from being overwritten in future updates.<br>
**StartRetrobatMarquees.bat**: Batch script to start RetroBat with the dynamic marquee system enabled.<br>
Compiling Scripts
Use PyInstaller to compile Python scripts into executables. Ensure Python 3.8 is installed, especially for 32-bit machines. Use the following command to prevent EmulationStation from losing focus:


<code>pyinstaller --onefile --noconsole ESEvents.py
pyinstaller --onefile --noconsole ESEventPush.py</code>


<h2>Configuring events.ini File</h2>
<p>
Configure events.ini to specify paths for marquees and other key settings like accepted formats, MPV path, etc. This file is crucial for the marquee system to function properly.
</p>

<h2>Usage</h2>
<p>
Place ESEventPush.exe in EmulationStation script folders where you want to enable marquee updates (e.g., game-selected or system-selected).
Locate ESEvents.exe and events.ini in an accessible location, always in \RetroBat\marquees\
Use RenameMarquees.bat to rename your existing marquees.
Start RetroBat with StartRetrobatMarquees.bat to activate the dynamic marquee system.
</p>

<h2>Notes</h2>
<p>
Ensure MPV is installed and properly configured, as it is used to display marquees.
Organize your marquee images according to the structure defined in events.ini.
</p>

<h2>Contributions</h2>
<p>
Contributions to this project are welcome. If you have enhancements or suggestions, feel free to contribute to the project or open an issue on GitHub.
</p>
