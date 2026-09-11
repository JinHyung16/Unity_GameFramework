using Newtonsoft.Json;

namespace Game_DataLoader
{
    public static class JsonSettings
    {
        public static readonly JsonSerializerSettings Default = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,

            MissingMemberHandling = MissingMemberHandling.Ignore,
        };
    }
}
