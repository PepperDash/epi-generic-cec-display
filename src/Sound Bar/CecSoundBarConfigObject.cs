using Newtonsoft.Json;
using System;

namespace PepperDash.Plugin.Display.CecDisplayDriver
{
	public class CecSoundBarPropertiesConfig
	{
		[JsonProperty("id")]
		public string Id { get; set; }

        [JsonProperty("pollIntervalMs")]
        public long pollIntervalMs { get; set; }

	}
}