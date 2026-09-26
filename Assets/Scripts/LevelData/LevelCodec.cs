using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ChestPush.LevelData
{
    public static class LevelCodec
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        public static LevelDefinition ReadLevel(string json) => JsonConvert.DeserializeObject<LevelDefinition>(json, Settings);
        public static LevelGraphDefinition ReadGraph(string json) => JsonConvert.DeserializeObject<LevelGraphDefinition>(json, Settings);
        public static string WriteLevel(LevelDefinition level) => JsonConvert.SerializeObject(level, Settings) + "\n";
        public static string WriteGraph(LevelGraphDefinition graph) => JsonConvert.SerializeObject(graph, Settings) + "\n";
    }
}
