<h1>Detailed Guide for Dynamic Marquees with RetroBat</h1>
<h2>Introduction</h2>
<p>
This guide provides an expert-level, comprehensive walkthrough on setting up and customizing dynamic marquees for your RetroBat arcade system. It covers the installation process, managing marquee images, and configuring the events.ini file for various scenarios.
</p>
<h3>Prerequisites</h3>
<p>
A Windows PC (Windows 8 or later) with RetroBat installed.
Two monitors setup.<br>
MPV Media Player <a href="https://mpv.io">[MPV's Website]</a> installed in a specific directory (e.g., C:\RetroBat\marquees\mpv\mpv.exe).<br>
Image Magick <a href="https://imagemagick.org">[Image Magick Website]</a> installed in a specific directory (e.g., C:\RetroBat\marquees\imagemagick\convert.exe).<br>
Marquee images downloaded and ready for organization.<br>
<h3>Installation and Setup</h3>
Install RetroBat, MPV Player and ImageMagick: Ensure RetroBat is set up correctly on your system. Check links to mpv and imagemagick in your events.ini in (e.g., C:\RetroBat\marquees\events.ini)<br>
</p>

<h2>Configuring events.ini</h2>
<p>
The events.ini file contains several settings that dictate how the marquee system behaves. Here's a breakdown of each setting:
</p>
<p>
<b>[Settings]</b><br>
<b>Language</b>: Sets the default language. Example: <b>fr</b> for French.<br>
<b>MarqueeWidth</b>: Width of the marquee images. Example: <b>1920</b>.<br>
<b>MarqueeHeight</b>: Height of the marquee images. Example: <b>360</b>.<br>
<b>MarqueeBorder</b>: Size of the border around the marquee images. Example: <b>30</b>.<br>
<b>MarqueeAutoConvert</b>: Convert image to png, marquee size, naming + "-topper" (true) or use current file (false). Example: <b>false</b>.<br> 
<b>AcceptedFormats</b>: List of accepted image or videos formats. Example: <b>jpg, png</b>.<br>
<b>RetroBatPath</b>: Path to the RetroBat directory. Example: <b>C:\RetroBat\</b>.<br>
<b>RomsPath</b>: Path to the directory where ROMs are stored. Example: <b>C:\RetroBat\roms\</b>.<br>
<b>DefaultImagePath</b>: Path to the default image displayed. Example: <b>C:\RetroBat\marquees\images\default.png</b>.<br>
<b>MarqueeImagePath</b>: Path to where marquee images are stored. Example: <b>C:\RetroBat\marquees\images\</b>.<br>
<b>MarqueeFilePath</b>: Format for marquee file paths. Example: <b>{system_name}-{game_name}</b>.<br>
<b>MarqueeImagePathDefault</b>: Default Path to where marquee images are stored. Example: <b>C:\RetroBat\roms\</b>.<br>
<b>MarqueeFilePathDefault</b>: Default Format for marquee file paths. Example: <b>{system_name}\images\{game_name}-marquee</b>.<br>
<b>MarqueeAutoConvert</b>: Resize and convert marquee image with MarqueeImagePath structure name + "-topper" suffix<br>
<b>MarqueeAutoScraping</b>: Dynamic scrap marquee image on screenscraper (add image url to scrap in a "scrap.pool" pool file) and download and save image in folder MarqueeImagePath with name structure MarqueeImagePath<br>
<b>MarqueeAutoScrapingDebug</b>: Save failed scraps in a "scrapfailed.pool" file in C:\RetroBat\marquees\ folder<br>
<b>SystemMarqueePath</b>: Path to system marquees. Example: <b>C:\RetroBat\emulationstation\.emulationstation\themes\es-theme-carbon\art\logos</b>.<br>
<b>SystemFilePath</b>: Format for system file paths. Example: <b>{system_name}</b>.<br>
<b>CollectionMarqueePath</b>: Path to collection marquees. Example: <b>C:\RetroBat\emulationstation\.emulationstation\themes\es-theme-carbon\art\logos</b>.<br>
<b>CollectionFilePath</b>: Format for collection file paths. Example: <b>auto-{collection_name}</b>.<br>
<b>CollectionAlternativNames</b>: Alternative names for collections. Example: <b>custom-, arcade (prefix/suffix)</b>.<br>
<b>CollectionCorrelation</b>: Mapping for special (automatic renaming to target your theme) collection names. Example: <b>recent:lastplayed, all:allgames, 2players:at2players, 4players:at4players, collections:custom-collections</b>.<br>
<b>IPCChannel</b>: Inter-process communication channel for MPV. Example: <b>\\.\pipe\mpv-pipe</b>.<br>
<b>ScreenNumber</b>: Screen number for displaying images. Example: <b>1</b> or <b>2</b>.<br>
<b>MPVPath</b>: Path to MPV executable. Example: <b>C:\RetroBat\marquees\mpv\mpv.exe</b>.<br>
<b>MPVLaunchCommand</b>: Command to launch MPV with parameters.<br>
<b>MPVKillCommand</b>: Command to terminate MPV. Example: <b>taskkill /IM mpv.exe /F</b>.<br>
<b>MPVTestCommand</b>: Command to test MPV status.<br>
<b>IMPath</b>: Path to ImageMagick's convert executable. Example: <b>C:\RetroBat\marquees\imagemagick\convert.exe</b>.<br>
<b>IMConvertCommand</b>: Command format for ImageMagick conversion.<br>
<b>host</b>: Host address for the server. Example: <b>127.0.0.1</b>.<br>
<b>port</b>: Port number for the server. Example: <b>8080</b>.<br>
<b>logFile</b>: Logs in ESEvents.log Example: <b>true</b>.<br><br>
<b>[Commands]</b><br>
<b>quit</b>: Command action for quitting.<br>
<b>reboot</b>: Command action for rebooting.<br>
<b>shutdown</b>: Command action for shutting down.<br>
<b>config-changed</b>: Command action when configuration changes.<br>
<b>controls-changed</b>: Command action when controls change.<br>
<b>settings-changed</b>: Command action when settings change.<br>
<b>theme-changed</b>: Command action when theme changes.<br>
<b>game-start</b>: Command action when a game starts.<br>
<b>game-end</b>: Command action when a game ends.<br>
<b>sleep</b>: Command action for sleep mode.<br>
<b>wake</b>: Command action for wake-up.<br>
<b>screensaver-start</b>: Command action when screensaver starts.<br>
<b>screensaver-stop</b>: Command action when screensaver stops.<br>
<b>screensaver-game-select</b>: Command action during screensaver game selection.<br>
<b>system-select</b>: Command action when a system is selected.<br>
<b>system-selected</b>: Command action after a system is selected.<br>
<b>game-select</b>: Command action when a game is selected.<br>
<b>game-selected</b>: Command action after a game is selected.<br><br>
<b>mpv-show-text</b>: Command action to push text into mpv when an image has been successfully scrapped.<br><br>
</p>
<h2>Managing Marquee Images</h2>
<h3>Game Marquees</h3>
<p>
To customize game marquee images, place your images in the directory specified by <b>MarqueeImagePath</b> in the configuration file. The naming structure of these images should follow the pattern defined in <b>MarqueeFilePath</b>. 
</p>
<p>
For example, if you have a game named "Super Mario" in the "NES" system, and <b>MarqueeFilePath</b> is set to <code>{system_name}\images\{game_name}-marquee</code>, the marquee image should be named "Super Mario-marquee.jpg" (assuming the image is a .jpg file). This file should be placed in the path like "C:\RetroBat\roms\nes\images\Super Mario-marquee.jpg", where "nes" is equivalent to the system's name similar to the name of the folder where its ROMs are stored.<br>
>> events.ini : <b>MarqueeFilePath</b>: Format for marquee file paths. Example: <b>{system_name}\images\{game_name}-marquee</b>
</p>
<h3>System Marquees</h3>
<p>
System marquees are used to represent the gaming systems or consoles. These images should be placed in the directory specified by <b>SystemMarqueePath</b>. The naming convention for system marquee images is defined by <b>SystemFilePath</b>. <br>
</p>
<p>
For instance, if <b>SystemFilePath</b> is set to <code>{system_name}</code>, and you want to set a marquee for the NES system, the image should be named "nes.jpg" (or "nes.png" depending on the file format). The full path for this image would be something like "C:\RetroBat\emulationstation\.emulationstation\themes\es-theme-carbon\art\logos\nes.jpg" (or nes.png).<br>
>> events.ini : <b>SystemFilePath</b>: Format for system file paths. Example: <b>{system_name}</b>
</p>
<h3>Collection Marquees</h3>
<p>
For custom collections, marquee images can be managed similarly. Set the path for these images using <b>CollectionMarqueePath</b> and define the naming structure with <b>CollectionFilePath</b>. For example, if <b>CollectionFilePath</b> is set to <code>auto-{collection_name}</code>, and you have a collection named "Classics", the marquee image should be named "auto-Classics.jpg" and placed accordingly.<br>
>> events.ini : <b>CollectionFilePath</b>: Format for collection file paths. Example: <b>auto-{collection_name}</b>
</p>
<p>
Remember to adjust the paths and file names according to your own setup and preferences. The configuration settings allow for a high degree of customization to match your specific requirements.
</p>
<h3>Default Marquee</h3>
<p>
The DefaultImagePath is used when a specific game or system marquee is not found. This could be a generic image indicating that no marquee is available for the selected game/system.
</p>
<h3>Additional Tips</h3>
<p>
For a more personalized marquee display with RetroBat, you can create configure in the .ini correlation's names to point to collecntions images like favorites / lastplayeed / 2players images.
It's possible to customize the marquee system further by editing the events.ini file. For instance, you can create different structures for game marquees or use different naming conventions for system and collections marquees.
</p>
<h2>Starting the Marquee System</h2>
<p>
Use StartRetrobatMarquees.bat to launch RetroBat with the dynamic marquee system enabled. This script initializes everything necessary for the marquee system to function.
You can also create a Windows startup shortcut to launch StartRetrobatMarquees.bat automatically.
</p>
<h3>EXPERT PYTHON (not necessary if you want just to install .exe)</h3>
<p>
Compile Python Scripts: <br>
Use PyInstaller to compile ESEvents.py and ESEventPush.py into executables. This ensures that EmulationStation does not lose focus when these scripts are executed. Use the command:
</p>
<code>pyinstaller --onefile --noconsole ESEvents.py</code>
<code>pyinstaller --onefile --noconsole ESEventPush.py</code>
<h4>Placement of Compiled Scripts:</h4>
<p>
Place ESEventPush.exe in each script folder within EmulationStation where you want to trigger marquee changes. Common folders include game-start, game-select or game-selected, system-select or system-selected.
Place ESEvents.exe in a central location, typically within the RetroBat\marquees directory.
Configure events.ini: This file is critical for specifying the paths and formats for your marquees. More detailed configuration is explained in the dedicated section.
</p>
