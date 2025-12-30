using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace SDRSharp.Tetra.MultiChannel
{
    public static class ChannelSettingsStore
    {
        private const string FileName = "SDRSharp.Tetra.MultiChannels.xml";

        public static string GetSettingsPath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SDRSharp");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, FileName);
        }

        public static List<ChannelSettings> Load()
        {
            var path = GetSettingsPath();
            try
            {
                if (!File.Exists(path))
                {
                    // Default example channel (user can edit in GUI)
                    return new List<ChannelSettings>
                    {
                        new ChannelSettings { Name = "TETRA-1", FrequencyHz = 0, Enabled = true }
                    };
                }

                using var fs = File.OpenRead(path);
                var ser = new XmlSerializer(typeof(List<ChannelSettings>));
                if (ser.Deserialize(fs) is List<ChannelSettings> list)
                {
                    // Ensure IDs exist
                    foreach (var ch in list)
                    {
                        if (ch.Id == Guid.Empty) ch.Id = Guid.NewGuid();
                    }
                    return list;
                }
            }
            catch
            {
                // ignore and fall back
            }

            return new List<ChannelSettings>();
        }

        public static void Save(List<ChannelSettings> channels)
        {
            var path = GetSettingsPath();
            using var fs = File.Create(path);
            var ser = new XmlSerializer(typeof(List<ChannelSettings>));
            ser.Serialize(fs, channels);
        }
    }
}
