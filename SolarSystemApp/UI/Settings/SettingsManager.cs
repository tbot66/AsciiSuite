using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolarSystemApp.UI.Settings
{
    internal static class SettingsManager
    {
        private const string FileName = "settings.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static GameSettings Load()
        {
            try
            {
                if (!File.Exists(FileName))
                    return new GameSettings();

                string json = File.ReadAllText(FileName);
                return JsonSerializer.Deserialize<GameSettings>(json, JsonOptions) ?? new GameSettings();
            }
            catch
            {
                return new GameSettings();
            }
        }

        public static bool Save(GameSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(FileName, json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
