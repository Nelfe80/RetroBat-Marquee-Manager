namespace RetroBatMarqueeManager.Infrastructure.Rendering
{
    /// <summary>
    /// Single scene object — ISO entry in Lua's gfx_objects{}.
    /// </summary>
    public class LayObject
    {
        public string          Name       { get; init; } = "";
        public LayObjectType   Type       { get; set; }
        public LayProperties   Properties { get; set; } = new();
        public int             Z          { get; set; }
        public bool            Updated    { get; set; }

        /// <summary>Cancels the in-flight animation on this object before starting a new one.</summary>
        public CancellationTokenSource? AnimCts { get; set; }
    }
}
