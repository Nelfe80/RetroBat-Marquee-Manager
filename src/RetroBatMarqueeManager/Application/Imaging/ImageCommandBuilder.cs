using System.Text;

namespace RetroBatMarqueeManager.Application.Imaging
{
    public class ImageCommandBuilder
    {
        private readonly StringBuilder _args = new();

        public ImageCommandBuilder AddInput(string path)
        {
            _args.Append($" \"{path}\"");
            return this;
        }

        public ImageCommandBuilder Resize(int width, int height)
        {
            // Standard resize
            _args.Append($" -resize {width}x{height}");
            return this;
        }

        public ImageCommandBuilder Background(string hexColor)
        {
            _args.Append($" -background {hexColor}");
            return this;
        }

        public ImageCommandBuilder Gravity(string type = "Center")
        {
            _args.Append($" -gravity {type}");
            return this;
        }

        public ImageCommandBuilder Extent(int width, int height)
        {
            // Fill canvas
            _args.Append($" -extent {width}x{height}");
            return this;
        }

        public ImageCommandBuilder Flatten()
        {
            _args.Append(" -flatten");
            return this;
        }
        
        public ImageCommandBuilder Output(string path)
        {
            _args.Append($" \"{path}\"");
            return this;
        }

        public string Build() => _args.ToString().Trim();
    }
}
