using Newtonsoft.Json;

namespace GenericCecDisplay
{
	public class GenericCecDisplayPropertiesConfig
	{
		[JsonProperty("id")]
		public string Id { get; set; }

        [JsonProperty("volumeUpperLimit")]
        public int VolumeUpperLimit { get; set; }

        [JsonProperty("volumeLowerLimit")]
        public int VolumeLowerLimit { get; set; }

        [JsonProperty("pollIntervalMs")]
        public long PollIntervalMs { get; set; }

        [JsonProperty("coolingTimeMs")]
        public uint CoolingTimeMs { get; set; }

        [JsonProperty("warmingTimeMs")]
        public uint WarmingTimeMs { get; set; }
	}
}