<h1>Detailed Guide for Dynamic Marquees with RetroBat</h1>
<h2>Introduction</h2>
<p>
This guide provides an expert-level, comprehensive walkthrough on setting up and customizing dynamic marquees for your RetroBat arcade system. It covers the installation process, managing marquee images, and configuring the events.ini file for various scenarios.
</p>
<h3>Prerequisites</h3>
<p>
A Windows PC (Windows 8 or later) with RetroBat installed.
Two monitors setup.
MPV Media Player installed in a specific directory (e.g., C:\RetroBat\marquees\mpv).
Marquee images downloaded and ready for organization.
Installation and Setup
Install RetroBat and MPV Player: Ensure RetroBat is set up correctly on your system and that MPV Player is installed in the directory where your marquees will be stored.
Compile Python Scripts: Use PyInstaller to compile ESEvents.py and ESEventPush.py into executables. This ensures that EmulationStation does not lose focus when these scripts are executed. Use the command:
</p>
<code>pyinstaller --onefile --noconsole ESEvents.py
pyinstaller --onefile --noconsole ESEventPush.py</code>

<h3>Placement of Compiled Scripts:</h3>
<p>
Place ESEventPush.exe in each script folder within EmulationStation where you want to trigger marquee changes. Common folders include game-start, game-select, system-select.
Place ESEvents.exe in a central location, typically within the RetroBat\marquees directory.
Configure events.ini: This file is critical for specifying the paths and formats for your marquees. More detailed configuration is explained in the next section.
</p>

<h2>Configuring events.ini</h2>
<p>
The events.ini file contains several settings that dictate how the marquee system behaves. Here's a breakdown of each setting:
</p>
<p>
**RetroBatPath** : The path to your RetroBat installation. Used as a reference to locate related components.<br>
**RomsPath** : The directory where your ROMs are stored. This path is used to check if a given system exists.<br>
**MarqueeImagePath** : Base path where marquee images are stored.<br>
**MarqueeFilePath** : Defines the file naming structure for game marquees. {system_name} and {game_name} are placeholders replaced dynamically based on the current game.<br>
**SystemMarqueePath** : Directory where system marquees (like MAME, NES) are stored. These images are used as marquees for the system itself.<br>
**SystemFilePath** : File naming structure for system marquees. Typically in the format {system_name}-logo.<br>
**DefaultImagePath** : Path to a default marquee image, used when a specific game or system marquee is not available.<br>
**AcceptedFormats** : Lists the image formats (like jpg, png) that the system can use for marquees.<br>
**IPCChannel** : Name of the IPC (Inter-Process Communication) channel for sending commands to the MPV player.<br>
**ScreenNumber** : Identifies the screen where marquees will be displayed. Useful for setups with multiple monitors. (value 1 or 2)<br>
**MPVPath** : Path to the MPV media player executable.<br>
**MPVLaunchCommand** : Command to launch MPV with necessary parameters for displaying marquees.<br>
**MPVKillCommand** : Command to kill any running instances of MPV. Ensures no conflicting or multiple instances of MPV.<br>
**MPVTestCommand** : Command to test if MPV is currently running. Used to ensure MPV is active before attempting to display a marquee.<br>
**host and port** : Settings for the Flask server (ESEvents.py). The server listens on this IP address and port for incoming requests.<br>
</p>
<h3>Commands Section</h3>
<p>
The [Commands] section defines the actions to be taken for different events triggered by EmulationStation. For example, game-start would execute a command to display the marquee for the started game.
</p>
<h2>Managing Marquee Images</h2>
<h3>Game Marquees</h3>
<p>
Place your game marquee images in the directory specified by MarqueeImagePath. The images should follow the naming structure defined in MarqueeFilePath.
Example: For a game "Super Mario" in the "NES" system, and if MarqueeFilePath is set to {system_name}-{game_name}, the marquee image should be named nes-Super Mario.jpg and placed in C:\RetroBat\marquees\images\nes-Super Mario.jpg. (system_name like rom's folders)
</p>
<h3>System Marquees</h3>
<p>
System marquees represent the gaming systems or consoles. Place these images in the SystemMarqueePath.
The naming convention is defined by SystemFilePath. For example, if it's {system_name}-logo, the NES system marquee should be named nes-logo.jpg.
Example path: C:\RetroBat\marquees\images\nes-logo.jpg.
</p>
<h3>Default Marquee</h3>
<p>
The DefaultImagePath is used when a specific game or system marquee is not found. This could be a generic image indicating that no marquee is available for the selected game/system.
</p>
<h3>Additional Tips</h3>
<p>
For a more personalized marquee display with RetroBat, you can create images named -recent.png or -favorites.jpg in the root of the DefaultImagePath directory.
It's possible to customize the marquee system further by editing the events.ini file. For instance, you can create different structures for game marquees or use different naming conventions for system marquees.
</p>
<h2>Starting the Marquee System</h2>
<p>
Use StartRetrobatMarquees.bat to start RetroBat with the dynamic marquee system enabled. This script initializes everything necessary for the marquee system to function.
You can also create a Windows startup shortcut to launch StartRetrobatMarquees.bat automatically.
</p>
