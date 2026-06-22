using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using System.Globalization;

namespace RetroBatMarqueeManager.Application.Services
{
    public class MameElement
    {
        public string Name { get; set; } = "";
        public string ImageFile { get; set; } = "";
        public int State { get; set; } = 0;
    }

    public class MameViewElement
    {
        public string Ref { get; set; } = "";
        public string Name { get; set; } = ""; // MAME output key (e.g., LampLeader, lamp0)
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public class MameView
    {
        public string Name { get; set; } = "";
        public List<MameViewElement> Elements { get; } = new();
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public class MameLayout
    {
        public string Directory { get; set; } = "";
        public Dictionary<string, MameElement> Elements { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, MameView> Views { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static class MameLayParser
    {
        public static MameLayout Parse(string layFilePath)
        {
            var layout = new MameLayout();
            if (!File.Exists(layFilePath)) return layout;

            layout.Directory = Path.GetDirectoryName(layFilePath) ?? "";

            try
            {
                var doc = XDocument.Load(layFilePath);
                var root = doc.Root;
                if (root == null) return layout;

                // Parse elements
                foreach (var elemNode in root.Elements("element"))
                {
                    var name = elemNode.Attribute("name")?.Value;
                    if (string.IsNullOrEmpty(name)) continue;

                    var imgNode = elemNode.Element("image");
                    if (imgNode != null)
                    {
                        var file = imgNode.Attribute("file")?.Value;
                        var stateStr = imgNode.Attribute("state")?.Value;
                        int.TryParse(stateStr, out int state);

                        if (!string.IsNullOrEmpty(file))
                        {
                            layout.Elements[name] = new MameElement
                            {
                                Name = name,
                                ImageFile = file,
                                State = state
                            };
                        }
                    }
                }

                // Parse views
                foreach (var viewNode in root.Elements("view"))
                {
                    var name = viewNode.Attribute("name")?.Value;
                    if (string.IsNullOrEmpty(name)) continue;

                    var view = new MameView { Name = name };
                    double maxX = 0;
                    double maxY = 0;

                    foreach (var viewElemNode in viewNode.Elements("element"))
                    {
                        var @ref = viewElemNode.Attribute("ref")?.Value;
                        var elemName = viewElemNode.Attribute("name")?.Value ?? ""; // Can be empty for static elements

                        var boundsNode = viewElemNode.Element("bounds");
                        if (boundsNode != null && !string.IsNullOrEmpty(@ref))
                        {
                            double.TryParse(boundsNode.Attribute("x")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double x);
                            double.TryParse(boundsNode.Attribute("y")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double y);
                            double.TryParse(boundsNode.Attribute("width")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double w);
                            double.TryParse(boundsNode.Attribute("height")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double h);

                            view.Elements.Add(new MameViewElement
                            {
                                Ref = @ref,
                                Name = elemName,
                                X = x,
                                Y = y,
                                Width = w,
                                Height = h
                            });

                            maxX = Math.Max(maxX, x + w);
                            maxY = Math.Max(maxY, y + h);
                        }
                    }

                    view.Width = maxX;
                    view.Height = maxY;
                    layout.Views[name] = view;
                }
            }
            catch
            {
                // Return whatever was successfully parsed
            }

            return layout;
        }
    }
}
