namespace RetroBatMarqueeManager.Core.Interfaces
{
    public interface IDmdConfigService
    {
        string Port { get; set; }
        int BaudRate { get; set; }
        bool IsEnabled { get; }
        
        void Save();
        void Load();
    }
}
