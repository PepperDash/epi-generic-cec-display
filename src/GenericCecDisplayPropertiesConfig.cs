using Newtonsoft.Json;

namespace GenericCecDisplay
{
	public class GenericCecDisplayPropertiesConfig
	{
		[JsonProperty("pollIntervalMs")]
        public long PollIntervalMs { get; set; }

        [JsonProperty("coolingTimeMs")]
        public uint CoolingTimeMs { get; set; }

        [JsonProperty("warmingTimeMs")]
        public uint WarmingTimeMs { get; set; }

		[JsonProperty("cecPowerSet")]
		public uint CecPowerSet { get; set; }
	}
}