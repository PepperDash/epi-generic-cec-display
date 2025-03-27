using Newtonsoft.Json;

namespace PepperDash.Essentials.Plugin.Generic.Cec.Display
{
	public class CecDisplayDriverPropertiesConfig
	{
		[JsonProperty("id")]
		public string Id { get; set; }

        [JsonProperty("volumeUpperLimit")]
        public int volumeUpperLimit { get; set; }

        [JsonProperty("volumeLowerLimit")]
        public int volumeLowerLimit { get; set; }

        [JsonProperty("pollIntervalMs")]
        public long pollIntervalMs { get; set; }

        [JsonProperty("coolingTimeMs")]
        public uint coolingTimeMs { get; set; }

        [JsonProperty("warmingTimeMs")]
        public uint warmingTimeMs { get; set; }
	}
}