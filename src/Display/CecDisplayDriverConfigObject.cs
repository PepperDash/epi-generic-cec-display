using Newtonsoft.Json;
using System.Collections.Generic;

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

        [JsonProperty("cecProfile")]
        public string CecProfile { get; set; }

        [JsonProperty("powerOnCommandHex")]
        public string PowerOnCommandHex { get; set; }

        [JsonProperty("powerOffCommandHex")]
        public string PowerOffCommandHex { get; set; }

        [JsonProperty("powerStatusCommandHex")]
        public string PowerStatusCommandHex { get; set; }

        [JsonProperty("inputCommandsHexByInputKey")]
        public Dictionary<string, string> InputCommandsHexByInputKey { get; set; }

        [JsonProperty("activeInputs")]
        public List<string> ActiveInputs { get; set; }

        [JsonProperty("powerOffRequiresInputCommand")]
        public bool? PowerOffRequiresInputCommand { get; set; }

        [JsonProperty("powerOffInputPreCommandHex")]
        public string PowerOffInputPreCommandHex { get; set; }
	}
}