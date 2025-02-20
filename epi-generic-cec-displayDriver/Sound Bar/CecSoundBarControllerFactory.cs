using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PepperDash.Plugin.Display.CecDisplayDriver
{
    public class CecSoundBarControllerFactory : EssentialsPluginDeviceFactory<CecSoundBarController>
    {
        public CecSoundBarControllerFactory()
        {
			MinimumEssentialsFrameworkVersion = "1.6.7";
            TypeNames = new List<string> {"GenericCECSoundbar"};
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

            var config = dc.Properties.ToObject<CecSoundBarPropertiesConfig>();

            if (config != null)
            {
                return new CecSoundBarController(dc.Key, dc.Name, config, comms);
            }

            Debug.Console(0, Debug.ErrorLogLevel.Error, "Unable to deserialize config for device {0}", dc.Key);
            return null;
        }

        #endregion
    }
}