<img src="https://github.com/Nelfe80/RetroBat-Marquee-Manager/blob/master/dist/images/logo.png" style="width:100%;">
<h1>RetroBat Marquee Manager (V3.1)</h1>
<h2>A Dynamic Marquees for RetroBat with Svg support / Auto-resizing / Dynamic Scraping / RetroAchievements (WIP)</h2>
<p>This project enables <b>dynamic display of marquees</b> on a secondary topper screen for RetroBat users on Windows 8+, utilizing custom scripts to manage the display based on user interactions.
</p>
<p>Thanks to Aynshe, Bob Morane and Retrobat's community testers. </p>
<p><a href="https://www.youtube.com/watch?v=AFS7f5RKJZo" target="_blank">
    <img src="https://img.youtube.com/vi/AFS7f5RKJZo/0.jpg" alt="Lien vers la vidéo YouTube">
</a></p>
<h2>Install</h2>
<p>
Important Setup Instructions<br>
Create a marquees folder : "\RetroBat\plugins\MarqueeManager\"<br>
Download the project and go to the /dist folder and copy all files in "\RetroBat\plugins\MarqueeManager\"<br>
In the "\RetroBat\plugins\MarqueeManager\" folder :<br>
- config.ini (settings file)<br>
- ESEvents.exe (events listener like game-selected/game-start/system-selected...)<br>
- StartRetrobatMarquees.bat (to launch without dynamic scraping) or StartRetrobatMarqueesAS.bat (to launche with Auto dynamic Scraping) or StartRetrobatMarqueesASRA.bat (with Auto-Scraping & RetroAchievements)...<br>
- ESEventsScrapTopper.exe (dynamic scraping listener, download image on screenscraper then rename and push scraped image in MarqueeImagePath\MarqueeFilePath)<br>
- ESRetroAchievements.exe (retro-achievements)<br>
- screenscraper.ini (dynamic scraping dictionnary)<br>
- retroachievements.ini (RA dictionnary)<br>
- systems.scrap (screenscraper systems ids)<br>
<br><br>
Click to install.bat to copy ESEventPush.bat in folders such as <br>
- "C:\RetroBat\emulationstation\.emulationstation\scripts\game-selected" >> update marquee when a game is selected<br>
- "C:\RetroBat\emulationstation\.emulationstation\scripts\system-selected" >> update marquee when a system is selected<br>
- "C:\RetroBat\emulationstation\.emulationstation\scripts\game-start" >> update marquee when a game start<br>
<b>Configuration File Setup</b>:<br>
Ensure that the ini file is correctly configured for proper operation of the executables.<br><br>
<b>Updating and installing dependencies if needed</b>:<br>
Download and install mpv and ImageMagick. These are essential for the functioning of the system.<br>
MPV to target screen and display images and videos : for mpv, visit their official website <a href="https://mpv.io">MPV's Website</a> and install it to the marquees directory, resulting in a path like "\RetroBat\plugins\MarqueeManager\mpv\mpv.exe".<br>
ImageMagick to convert (svg to png), resize and optimize images : for ImageMagick, visit <a href="https://imagemagick.org">ImageMagick's Website</a> and install it similarly in the marquees directory. This should result in a path like "\RetroBat\plugins\MarqueeManager\imagemagick\convert.exe".<br>
By following these instructions, you'll ensure that ESEvents.exe and ESEventPush.exe are correctly placed, and the necessary tools (mpv and ImageMagick) are installed and configured for optimal performance.
</p>
<h2>Configuring config.ini File</h2>
<p>
Configure config.ini to specify paths for marquees and other key settings like accepted formats, MPV path and ImageMagick path, etc. This file is crucial for the marquee system to function properly. (MarqueeRetroAchievements = true in config.ini file to activate RetroAchievements or MarqueeAutoScraping = true to scrap banners...)
</p>
<h2>Download and install marquees</h2>
You can download marquees here then install in the default folder "\RetroBat\plugins\MarqueeManager\images\" :<br>Use the format {system_name}-{game_name}.ext. For example, for Mario on NES, use nes-mario.jpg. (game_name = rom name without ext, system_name = system folder)<br>
Launchbox Games Database : <a href="https://gamesdb.launchbox-app.com/">https://gamesdb.launchbox-app.com/</a><br>
Pixelcade Forums : <a href="https://pixelcade.org/forum/art-exchange-lcd/a-few-lcd-marquees-links/#post-2071">https://pixelcade.org/forum/art-exchange-lcd/a-few-lcd-marquees-links/#post-2071</a><br>
<p></p>
<h2>Scrapping usage</h2>
<p>If you're missing any marquee, you can activate <b>dynamic scrapping</b> (MarqueeAutoScraping = true in config.ini file), which allows the system to download marquee image (the real toppers, not the logos) of arcade machines directly if it detects that a marquee is missing, without having to go through the RetroBat scraper "screenscraper" (check your screenscraper id/password in Retrobat). Once scrapping is complete, a small message appears on your marquee's screen like "Metal Slug 3 topper successfully scraped!". If you reselect or play the game again, the marquee will be updated. If you don't see the marquee appear, there may be no marquee image for that game in the screenscraper image database. The "Screenmarquee" images are only downloaded if there is no "Marquee" image. If the scraperfailed option is enabled, failed scraping will be indicated in a scrapfailed.pool file. I encourage you to take part in the screenscraper visual enhancement if you want to see a marquee appear or put it manually in your marquee folder.</p>
<h3>How to Scrape Marquees from RetroBat</h3>
<p>
If you plan to use scraped marquees or incorporate your own custom marquee images into the system, please be aware of an issue in the scraping process. Both logos and marquee images are currently saved with the same suffix <b>-marquee</b> at the end of the file name. This can lead to confusion and potential file conflicts within the system.
</p>
<p>
To scrape marquees directly within RetroBat:
<ol>
<li>Access the scraping menu in RetroBat.</li>
<li>Choose to scrape from SCREENSCRAPER, HFSDB or ARCADEDB in the scraper options.</li>
<li>In the 'Logo Source' option, select 'Banner' to obtain real topper marquees.</li>
</ol>
This approach allows you to scrape specific marquee images that are more suited for use as toppers.
</p>
<p>
After scraping, you might encounter the situation where both marquees and logos are labeled with <b>-marquee</b>. To resolve this, use the script in tools folder, <b>RenameMarquees.bat</b> (edit file with notepad to change folder link). This script will rename all marquee images, changing the <b>-marquee</b> suffix to <b>-marqueescraped</b>. This renaming step is crucial for ensuring that marquee images are properly recognized and prioritized by the system. Moreover, it allows you to rescrape for the actual logos without overwriting the marquees you've just scraped. Once the script has been executed, and the marquee images are renamed to include <b>-marqueescraped.png</b>, you can safely scrape again to obtain the true <b>-marquee</b> logos without any file conflicts.
</p>
<h2>START</h2>
<h3>Launch Start.bat</h3>
<br><br>
<h2>Notes</h2>
<p>
For visual pinball, don't forget to update VPinMame
</p>
<p>
It is important to note that SVG files may require additional processing time during their first use. However, once they are converted to PNG format, you will experience smoother navigation and quicker access to these images within the system.
</p>
<p>
Ensure MPV and IMAGEMAGICK are installed in \RetroBat\plugins\MarqueeManager\ directory.
Organize your marquee images according to the structure defined in config.ini.
</p>
<h2>New Feature: DMD Screen Support</h2>
<p>
With the latest update, DMD screen support is now available. To enable this, you need to rename `config-dmd.ini` to `config.ini` and adjust the path to `dmd.exe` in your configuration file. Make sure the path correctly points to the location of `dmd.exe` to utilize the new display capabilities.
</p>


