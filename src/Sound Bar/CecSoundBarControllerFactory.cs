using System;
using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;
using Serilog.Events;

namespace PepperDash.Plugin.Display.CecDisplayDriver
{
    public class CecSoundBarControllerFactory : EssentialsPluginDeviceFactory<CecSoundBarController>
    {
        public CecSoundBarControllerFactory()
        {
			MinimumEssentialsFrameworkVersion = "2.0.0";
            TypeNames = new List<string> {"GenericCECSoundbar"};
        }
        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            //Debug.LogMessage(LogEventLevel.Information, "Building CEC Soundbar {key}", null, dc.Key);

            IBasicCommunication comms;
            try
            {
                comms = CommFactory.CreateCommForDevice(dc);
            }
            catch(Exception ex)
            {
                Debug.LogMessage(ex, "Exception getting comms");
                comms = null;
            }

            if (comms == null)
            {
                Debug.LogMessage(LogEventLevel.Error, "Unable to create comms for device {key}", null, dc.Key);
                return null;
            }
            try
            {
                var config = dc.Properties.ToObject<CecSoundBarPropertiesConfig>();

                
                return new CecSoundBarController(dc.Key, dc.Name, config, comms);
                
            }
            catch (Exception ex)
            {
                Debug.LogMessage(ex, "Unable to create comms for device {key}", null, dc.Key);
                return null;
            }
        }        
    }
}