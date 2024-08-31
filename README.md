<img src="https://github.com/Nelfe80/RetroBat-Marquee-Manager/blob/master/dist/images/logo.png" style="width:100%;">
<h1>RetroBat Marquee Manager (V3.3)</h1>
<h2>A Dynamic Marquees for RetroBat with Svg support / Auto-resizing / Dynamic Scraping / RetroAchievements (WIP)</h2>
<p>This project enables <b>dynamic display of marquees</b> on a secondary topper screen for RetroBat users on Windows 8+, utilizing custom scripts to manage the display based on user interactions.
</p>
<p>Thanks to Aynshe, Bob Morane and Retrobat's community testers. </p>
<p>
    <a href="https://www.youtube.com/watch?v=7LwR_cwa0Cg" target="_blank">
        <img src="https://i.ytimg.com/vi/7LwR_cwa0Cg/hqdefault.jpg" alt="Lien vers la vidéo YouTube">
    </a>
    <a href="https://www.youtube.com/watch?v=AFS7f5RKJZo" target="_blank">
        <img src="https://i.ytimg.com/vi/AFS7f5RKJZo/hqdefault.jpg" alt="Lien vers la vidéo YouTube">
    </a>
</p>
<h2>Install</h2>
<p>
To install the Marquee Manager in RetroBat, follow these steps:<br>
- Access the Main Menu: Start by launching RetroBat. Once you're on the main screen, navigate to the Main Menu.<br>
- Updates and Downloads: In the Main Menu, look for the Updates and Downloads section. Select it to proceed.<br>
- Download Content: Within the Updates and Downloads menu, find and select the Download Content option.<br>
- Media Tab: After selecting Download Content, switch to the Media tab.<br>
- Select Marquee Manager: In the Media tab, you will see a list of media options available for download. Look for and select Marquee Manager from the list.<br>
- Use BatGui for Configuration: Once the Marquee Manager is installed, you can use BatGui to further configure the settings. This includes selecting which screen to target for the marquee display and deciding whether to activate any additional modules.<br>
</p>
</p>
<h2>Configuring config.ini File if needed</h2>
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
With the latest update, DMD screen support is now available only for ZeDMD (install esp32 fw : https://github.com/zesinger/ZeDMD_Updater/releases ) and create DMD ( https://www.youtube.com/watch?v=hgdIUG90M0c ) . To enable this, you need to rename `config-dmd.ini` to `config.ini` and adjust the path to `dmd.exe` in your configuration file. Make sure the path correctly points to the location of `dmd.exe` to utilize the new display capabilities.
</p>


