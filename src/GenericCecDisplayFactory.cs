using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace GenericCecDisplay
{
    public class GenericCecDisplayFactory : EssentialsPluginDeviceFactory<GenericCecDisplayController>
    {
        public GenericCecDisplayFactory()
        {
			MinimumEssentialsFrameworkVersion = "1.6.7";
            TypeNames = new List<string> {"GenericCecDisplay"};
        }

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            var comms = CommFactory.CreateCommForDevice(dc);
            if (comms == null)
            {
                Debug.Console(0, Debug.ErrorLogLevel.Error, "Unable to create comms for device {0}", dc.Key);
                return null;
            }

            var config = dc.Properties.ToObject<GenericCecDisplayPropertiesConfig>();
	        if (config != null) return new GenericCecDisplayController(dc.Key, dc.Name, config, comms);
	        
			Debug.Console(0, Debug.ErrorLogLevel.Error, "Unable to deserialize config for device {0}", dc.Key);
	        return null;
        }
    }
}