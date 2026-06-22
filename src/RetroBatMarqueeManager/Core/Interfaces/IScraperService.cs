using System;
using System.Threading.Tasks;

namespace RetroBatMarqueeManager.Core.Interfaces
{
    public interface IScraperService
    {
        string Name { get; }
        event Action<string, string, string?> OnScrapeCompleted;
        Task<string?> CheckAndScrapeAsync(string systemName, string gameName, string gamePath, string mediaType);
        bool IsScraping(string systemName, string gameName, string mediaType);
    }
}
