using System.Collections.Generic;
using PepperDash.Core;
using Serilog.Events;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PepperDash.Essentials.Plugins.Display
{
    public class CecDisplayDriverControllerFactory : EssentialsPluginDeviceFactory<CecDisplayDriverDisplayController>
    {
        public CecDisplayDriverControllerFactory()
        {
			MinimumEssentialsFrameworkVersion = "3.0.0";
            TypeNames = new List<string> {"GenericCecDisplay"};
        }

        #region Overrides of EssentialsDeviceFactory<SamsungMdcDisplayController>

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            var controlConfig = CommFactory.GetControlPropertiesConfig(dc);

            var comms = CommFactory.CreateCommForDevice(dc);

            if (comms == null)
            {
                Debug.LogMessage(LogEventLevel.Error, "Unable to create comms for device {0}", dc.Key);
                return null;
            }

            var config = dc.Properties.ToObject<CecDisplayDriverPropertiesConfig>();

            if (config != null)
            {
                return new CecDisplayDriverDisplayController(
                    dc.Key,
                    dc.Name,
                    config,
                    comms,
                    controlConfig != null ? controlConfig.ControlPortDevKey : null,
                    controlConfig != null ? controlConfig.ControlPortName : null
                );
            }

            Debug.LogMessage(LogEventLevel.Error, "Unable to deserialize config for device {0}", dc.Key);
            return null;
        }

        #endregion
    }
}