using System;
using System.Threading.Tasks;

namespace RetroBatMarqueeManager.Core.Interfaces
{
    public interface IScraperManager
    {
        event Action<string, string, string?> OnScrapeCompleted;
        Task<string?> CheckAndScrapeAsync(string systemName, string gameName, string gamePath, string mediaType);
        bool IsScraping(string systemName, string gameName, string mediaType);
        string? GetActiveScraperName(string systemName, string gameName, string mediaType);
    }
}
