<h2>Configuration File (ini) Guide</h2>
<h3>[Settings]</h3>
<p>
Language: Sets the default language. Example: fr for French.<br>
MarqueeWidth: Width of the marquee images. Example: 1920.<br>
MarqueeHeight: Height of the marquee images. Example: 360.<br>
MarqueeBorder: Size of the border around the marquee images. Example: 30.<br>
AcceptedFormats: List of accepted image formats. Example: jpg, png.<br>
RetroBatPath: Path to the RetroBat directory. Example: C:\RetroBat\.<br>
RomsPath: Path to the directory where ROMs are stored. Example: C:\RetroBat\roms\.<br>
DefaultImagePath: Path to the default image displayed. Example: C:\RetroBat\marquees\images\default.png.<br>
MarqueeImagePath: Path to where marquee images are stored. Example: C:\RetroBat\roms\.<br>
MarqueeFilePath: Format for marquee file paths. Example: {system_name}\images\{game_name}-marquee.<br>
SystemMarqueePath: Path to system marquees. Example: C:\RetroBat\emulationstation\.emulationstation\themes\es-theme-carbon\art\logos.<br>
SystemFilePath: Format for system file paths. Example: {system_name}.<br>
CollectionMarqueePath: Path to collection marquees. Example: C:\RetroBat\emulationstation\.emulationstation\themes\es-theme-carbon\art\logos.<br>
CollectionFilePath: Format for collection file paths. Example: auto-{collection_name}.<br>
CollectionAlternativNames: Alternative names for collections. Example: custom-, arcade.<br>
CollectionCorrelation: Mapping for special collection names. Example: recent:lastplayed, all:allgames.<br>
IPCChannel: Inter-process communication channel for MPV. Example: \\.\pipe\mpv-pipe.<br>
ScreenNumber: Screen number for displaying images. Example: 1.<br>
MPVPath: Path to MPV executable. Example: C:\RetroBat\marquees\mpv\mpv.exe.<br>
MPVLaunchCommand: Command to launch MPV with parameters.<br>
MPVKillCommand: Command to terminate MPV. Example: taskkill /IM mpv.exe /F.<br>
MPVTestCommand: Command to test MPV status.<br>
IMPath: Path to ImageMagick's convert executable. Example: C:\RetroBat\marquees\imagemagick\convert.exe.<br>
IMConvertCommand: Command format for ImageMagick conversion.<br>
host: Host address for the server. Example: 127.0.0.1.<br>
port: Port number for the server. Example: 8080.
</p>
<h3>[Commands]</h3>
<p>
quit: Command action for quitting.<br>
reboot: Command action for rebooting.<br>
shutdown: Command action for shutting down.<br>
config-changed: Command action when configuration changes.<br>
controls-changed: Command action when controls change.<br>
settings-changed: Command action when settings change.<br>
theme-changed: Command action when theme changes.<br>
game-start: Command action when a game starts.<br>
game-end: Command action when a game ends.<br>
sleep: Command action for sleep mode.<br>
wake: Command action for wake-up.<br>
screensaver-start: Command action when screensaver starts.<br>
screensaver-stop: Command action when screensaver stops.<br>
screensaver-game-select: Command action during screensaver game selection.<br>
system-select: Command action when a system is selected.<br>
system-selected: Command action after a system is selected.<br>
game-select: Command action when a game is selected.<br>
game-selected: Command action after a game is selected.<br>
</p>
