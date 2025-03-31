using Newtonsoft.Json;

namespace PepperDash.Essentials.Plugin.Generic.Cec.SoundBar
{
	public class CecSoundBarPropertiesConfig
	{
		[JsonProperty("id")]
		public string Id { get; set; }

        [JsonProperty("pollIntervalMs")]
        public long pollIntervalMs { get; set; }

		[JsonProperty("powerOnUsesDiscreteCommand")]
		public bool PowerOnUsesDiscreteCommand { get; set; }

    }
}