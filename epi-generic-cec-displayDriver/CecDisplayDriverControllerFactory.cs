using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PepperDash.Plugin.Display.CecDisplayDriver
{
    public class CecDisplayDriverControllerFactory : EssentialsPluginDeviceFactory<CecDisplayDriverDisplayController>
    {
        public CecDisplayDriverControllerFactory()
        {
			MinimumEssentialsFrameworkVersion = "1.6.7";
            TypeNames = new List<string> {"GenericCecDisplay"};
        }

        #region Overrides of EssentialsDeviceFactory<SamsungMdcDisplayController>

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {

            var comms = CommFactory.CreateCommForDevice(dc);

            if (comms == null)
            {
                Debug.Console(0, Debug.ErrorLogLevel.Error, "Unable to create comms for device {0}", dc.Key);
                return null;
            }

            var config = dc.Properties.ToObject<CecDisplayDriverPropertiesConfig>();

            if (config != null)
            {
                return new CecDisplayDriverDisplayController(dc.Key, dc.Name, config, comms);
            }

            Debug.Console(0, Debug.ErrorLogLevel.Error, "Unable to deserialize config for device {0}", dc.Key);
            return null;
        }

        #endregion
    }
}