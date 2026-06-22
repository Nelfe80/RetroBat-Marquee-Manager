using System.Diagnostics;
using System.Text;
using RetroBatMarqueeManager.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RetroBatMarqueeManager.Application.Services
{
    /// <summary>
    /// EN: Service dedicated to video marquee generation and manipulation (separated from ImageConversionService)
    /// FR: Service dédié à la génération et manipulation de vidéos marquee (séparé de ImageConversionService)
    /// </summary>
    public class VideoMarqueeService
    {
        private readonly IConfigService _config;
        private readonly IProcessService _processService;
        private readonly ILogger<VideoMarqueeService> _logger;
        private readonly VideoOffsetStorageService _offsetStorage;

        public VideoMarqueeService(
            IConfigService config,
            IProcessService processService,
            VideoOffsetStorageService offsetStorage,
            ILogger<VideoMarqueeService> logger)
        {
            _config = config;
            _processService = processService;
            _offsetStorage = offsetStorage;
            _logger = logger;
            
            // EN: Initialize individual offset base directory
            // FR: Initialiser le répertoire de base des offsets individuels
            var parentDir = Directory.GetParent(_config.CachePath);
            if (parentDir != null)
            {
                var subFolder = _config.GenerateMarqueeVideoFolder;
                if (string.IsNullOrWhiteSpace(subFolder)) subFolder = "generated_videos";
                _offsetStorage.SetIndividualBaseDirectory(Path.Combine(parentDir.FullName, subFolder));
            }
        }

        /// <summary>
        /// EN: Capture a single frame from a video at specified timestamp
        /// FR: Capturer une frame unique d'une vidéo à un timestamp spécifié
        /// </summary>
        /// <param name="videoPath">Path to source video</param>
        /// <param name="timestamp">Timestamp in seconds (default: 5.0)</param>
        /// <param name="outputPath">Optional custom output path</param>
        /// <returns>Path to captured frame (PNG)</returns>
        public string? CaptureVideoFrame(string videoPath, double timestamp = 5.0, string? outputPath = null)
        {
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            {
                _logger.LogError($"[VideoCapture] Source video not found: {videoPath}");
                return null;
            }

            try
            {
                var ffmpeg = FindFfmpeg();
                if (string.IsNullOrEmpty(ffmpeg))
                {
                    _logger.LogError("[VideoCapture] FFmpeg not found");
                    return null;
                }

                // EN: Default output to cache directory
                // FR: Sortie par défaut dans le répertoire cache
                if (string.IsNullOrEmpty(outputPath))
                {
                    var cacheDir = Path.Combine(_config.CachePath, "video_preview");
                    if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                    outputPath = Path.Combine(cacheDir, "preview_frame.png");
                }

                _logger.LogInformation($"[VideoCapture] Capturing frame at {timestamp}s from: {Path.GetFileName(videoPath)}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // EN: FFmpeg command: Seek to timestamp, extract 1 frame
                // FR: Commande FFmpeg : Aller au timestamp, extraire 1 frame
                startInfo.ArgumentList.Add("-y"); // Overwrite
                startInfo.ArgumentList.Add("-ss");
                startInfo.ArgumentList.Add(timestamp.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)); // EN: Use InvariantCulture for decimal point / FR: Utiliser InvariantCulture pour point décimal
                startInfo.ArgumentList.Add("-i");
                startInfo.ArgumentList.Add(videoPath);
                startInfo.ArgumentList.Add("-frames:v");
                startInfo.ArgumentList.Add("1");
                startInfo.ArgumentList.Add("-q:v");
                startInfo.ArgumentList.Add("2"); // High quality
                startInfo.ArgumentList.Add(outputPath);

                using (var process = Process.Start(startInfo))
                {
                    if (process == null) return null;

                    var stderr = new StringBuilder();
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(10000)) // 10s timeout
                    {
                        _logger.LogError("[VideoCapture] FFmpeg timeout");
                        process.Kill();
                        return null;
                    }

                    if (process.ExitCode != 0)
                    {
                        _logger.LogError($"[VideoCapture] FFmpeg failed: {stderr.ToString()}");
                        return null;
                    }
                }

                if (File.Exists(outputPath))
                {
                    _logger.LogInformation($"[VideoCapture] Frame captured successfully: {outputPath}");
                    return outputPath;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VideoCapture] Exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// EN: Check if a video path corresponds to a generated marquee video
        /// FR: Vérifier si un chemin vidéo correspond à une marquee vidéo générée
        /// </summary>
        public bool IsGeneratedVideo(string videoPath)
        {
            if (string.IsNullOrEmpty(videoPath)) return false;
            
            var generatedFolder = _config.GenerateMarqueeVideoFolder;
            if (string.IsNullOrWhiteSpace(generatedFolder)) generatedFolder = "generated_videos";
            
            return videoPath.Contains(Path.DirectorySeparatorChar + generatedFolder + Path.DirectorySeparatorChar) ||
                   videoPath.Contains(Path.AltDirectorySeparatorChar + generatedFolder + Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// EN: Generate marquee video with custom offsets  
        /// FR: Générer vidéo marquee avec offsets personnalisés
        /// </summary>
        public string? GenerateVideoWithOffsets(
            string sourceVideo,
            string logoPath,
            string system,
            string gameName,
            VideoOffsetData offsets)
        {
            if (string.IsNullOrEmpty(sourceVideo) || !File.Exists(sourceVideo))
            {
                _logger.LogError($"[VideoGen] Source video not found: {sourceVideo}");
                return null;
            }

            try
            {
                var ffmpeg = FindFfmpeg();
                if (string.IsNullOrEmpty(ffmpeg))
                {
                    _logger.LogError("[VideoGen] FFmpeg not found");
                    return null;
                }

                // EN: Determine output path
                // FR: Déterminer le chemin de sortie
                var subFolder = _config.GenerateMarqueeVideoFolder;
                if (string.IsNullOrWhiteSpace(subFolder)) subFolder = "generated_videos";

                var parentDir = Directory.GetParent(_config.CachePath);
                if (parentDir == null) return null;

                string videoCacheDir = Path.Combine(parentDir.FullName, subFolder, system);
                if (!Directory.Exists(videoCacheDir)) Directory.CreateDirectory(videoCacheDir);

                string targetPath = Path.Combine(videoCacheDir, $"{gameName}.mp4");

                _logger.LogInformation($"[VideoGen] Generating video with offsets for {system}/{gameName}");

                var mw = _config.MarqueeWidth;
                var mh = _config.MarqueeHeight;

                // EN: Build FFmpeg filter with offsets
                // FR: Construire le filtre FFmpeg avec offsets
                string filter = BuildFilterWithOffsets(mw, mh, offsets, !string.IsNullOrEmpty(logoPath) && File.Exists(logoPath));
                _logger.LogInformation($"[VideoGen] Generated Filter: {filter}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var args = new List<string> { "-y" }; // Overwrite
                args.Add("-i"); args.Add(sourceVideo);

                bool hasLogo = !string.IsNullOrEmpty(logoPath) && File.Exists(logoPath);
                if (hasLogo)
                {
                    args.Add("-i"); args.Add(logoPath);
                    args.Add("-filter_complex"); args.Add(filter);
                }
                else
                {
                    args.Add("-vf"); args.Add(filter);
                }

                args.Add("-c:v"); args.Add("libopenh264");
                args.Add("-preset"); args.Add("veryfast");
                args.Add("-crf"); args.Add("23");
                args.Add("-an"); // No audio
                args.Add(targetPath);

                foreach (var arg in args) startInfo.ArgumentList.Add(arg);

                using (var process = Process.Start(startInfo))
                {
                    if (process == null) return null;

                    var stderr = new StringBuilder();
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(60000)) // 60s timeout
                    {
                        _logger.LogError("[VideoGen] FFmpeg timeout");
                        process.Kill();
                        return null;
                    }

                    if (process.ExitCode != 0)
                    {
                        _logger.LogError($"[VideoGen] FFmpeg failed: {stderr.ToString()}");
                        return null;
                    }
                }

                if (File.Exists(targetPath))
                {
                    _logger.LogInformation($"[VideoGen] Video generated successfully: {targetPath}");
                    
                    // EN: Save individual offsets after successful generation
                    // FR: Sauvegarder les offsets individuels après génération réussie
                    _offsetStorage.SaveIndividualOffsets(system, gameName, offsets);
                    
                    return targetPath;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VideoGen] Exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// EN: Build FFmpeg filter string with custom offsets
        /// FR: Construire la chaîne de filtre FFmpeg avec offsets personnalisés
        /// </summary>
        private string BuildFilterWithOffsets(int targetWidth, int targetHeight, VideoOffsetData offsets, bool hasLogo)
        {
            var sb = new StringBuilder();

            // EN: Video processing: crop → scale → format
            // FR: Traitement vidéo : recadrage → mise à l'échelle → format
            sb.Append("[0:v]");

            // Crop (if offsets defined)
            if (offsets.CropWidth > 0 && offsets.CropHeight > 0)
            {
                sb.Append($"crop={offsets.CropWidth}:{offsets.CropHeight}:{offsets.CropX}:{offsets.CropY},");
            }

            // Scale with zoom
            int scaledWidth = (int)(targetWidth * offsets.Zoom);
            int scaledHeight = (int)(targetHeight * offsets.Zoom);
            sb.Append($"scale={scaledWidth}:{scaledHeight}:force_original_aspect_ratio=decrease,");
            sb.Append($"pad={targetWidth}:{targetHeight}:(ow-iw)/2:(oh-ih)/2,");
            sb.Append("format=yuv420p");

            if (hasLogo)
            {
                sb.Append("[base];");

                // Logo processing: scale within constraints
                // EN: Adaptive Scaling based on Aspect Ratio
                // If AR > 2.5 (Ultrawide/Marquee): Use 80% Height (Logo needs to be big to be seen on strip)
                // If AR <= 2.5 (Standard/Box 16:9, 4:3, 2:1): Use 40% Height (Logo shouldn't dominate the video)
                
                double ar = (double)targetWidth / targetHeight;
                double heightFactor = (ar > 2.5) ? 0.8 : 0.4;
                
                int maxW = (int)(targetWidth * 0.90);
                int maxH = (int)(targetHeight * heightFactor * offsets.LogoScale);
                
                sb.Append($"[1:v]scale={maxW}:{maxH}:force_original_aspect_ratio=decrease[logo];");

                // Overlay logo at custom position
                sb.Append($"[base][logo]overlay={offsets.LogoX}:{offsets.LogoY},format=yuv420p");
            }

            return sb.ToString();
        }

        private string? FindFfmpeg()
        {
            // EN: Search FFmpeg in tools folder or PATH
            // FR: Rechercher FFmpeg dans le dossier tools ou PATH
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(toolsPath)) return toolsPath;

            var mpvDir = Path.GetDirectoryName(_config.MPVPath);
            if (!string.IsNullOrEmpty(mpvDir))
            {
                var mpvFfmpeg = Path.Combine(mpvDir, "ffmpeg.exe");
                if (File.Exists(mpvFfmpeg)) return mpvFfmpeg;
            }

            // Try PATH
            try
            {
                var result = Process.Start(new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "ffmpeg",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (result != null)
                {
                    var output = result.StandardOutput.ReadToEnd();
                    result.WaitForExit();
                    if (result.ExitCode == 0)
                    {
                        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0) return lines[0].Trim();
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
