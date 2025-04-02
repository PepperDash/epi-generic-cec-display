using System;
using System.Collections.Generic;
using System.Text;
using Crestron.SimplSharp;
using PepperDash.Core;
using PepperDash.Core.Logging;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Generic.Cec.SoundBar
{
    public class CecSoundBarController : EssentialsDevice, ICommunicationMonitor, IHasPowerControlWithFeedback
    {
        public const int InputPowerOn = 101;
        public const int InputPowerOff = 102;
        public static List<string> InputKeys = new List<string>();
        private readonly CecSoundBarPropertiesConfig _config;
        private readonly bool _powerOnUsesDiscreteCommand;

        private readonly long _pollIntervalMs;



        public List<BoolFeedback> InputFeedback;


        private RoutingInputPort _currentInputPort;

        private byte[] _incomingBuffer = { };

        public IntFeedback InputNumberFeedback;

        public int CurrentInputNumber
        {
            get
            {
                return _CurrentInputNumber;
            }
            private set
            {
                _CurrentInputNumber = value;
                InputNumberFeedback.FireUpdate();
                UpdateBooleanFeedback();
            }
        }
        private int _CurrentInputNumber;

        private bool _isPoweringOnIgnorePowerFb;

        private CCriticalSection _parseLock = new CCriticalSection();
        private bool _powerIsOn;


        /// <summary>
        /// Constructor for IBaseCommunication
        /// </summary>
        /// <param name="name"></param>
        /// <param name="config"></param>
        /// <param name="key"></param>
        /// <param name="comms"></param>
        //public SamsungMdcDisplayController(string key, string name, DeviceConfig config) : base(key, name)
        public CecSoundBarController(string key, string name, CecSoundBarPropertiesConfig config,
            IBasicCommunication comms)
            : base(key, name)
        {
            Communication = comms;
            Communication.BytesReceived += Communication_BytesReceived;
            _config = config;

            Id = _config.Id == null ? (byte)0x01 : Convert.ToByte(_config.Id, 16);
            _powerOnUsesDiscreteCommand = _config.PowerOnUsesDiscreteCommand;

            _pollIntervalMs = _config.pollIntervalMs;

            PowerIsOnFeedback = new BoolFeedback(() => _powerIsOn);

            Init();
        }

        public IBasicCommunication Communication { get; private set; }
        public byte Id { get; private set; }
        public IntFeedback StatusFeedback { get; set; }


        /// <summary>
        /// 
        /// </summary>


        #region Command Constants

        /// <summary>
        /// Power control on - when triggered switches to the display ARC input
        /// </summary>
        public const string PowerOnCmd = "\x4F\x82\x10\x00";
        //Note
        //  "\x10" == ?
        //  "\x11" == ARC input
        //  "\x12" == ?
        //  we need "Active Source Optical" or discrete Power On may also be "One Touch Play"

        public const string PowerOnTv = "\x4F\x82\x10\x00";
        // returns: \x5F\x72\x01

        public const string PowerOnArcCmd = "\x4F\x82\x11\x00";
        // returns: \x5F\x72\x01 ** only when triggered after an OFF command

        public const string PowerOnOpticalCmd = "\x4F\x82\x12\x00";
        // returns: \x5F\x72\x01 ** only when triggered after an OFF command

        /// <summary>
        /// Discrete power on - when triggered, should not switch to an input
        /// </summary>
        public const string PowerOnDiscreteCmd = "\x45\x44\x6D";

        public const string PowerOnHdmiCmd = "\x4F\x82\x41\x00";

        public const string PowerOnInputXCmd = "\x4F\x82\xFF\x00";
        
        public const string GetAddressCmd = "\x45\x83";

        /// <summary>
        /// Power control off
        /// </summary>
        public const string PowerOffCmd = "\x1F\x36";
        // returns: \x5F\x72\x00

        /*
        in room, switched soundbar to HDMI from front panel and recieved the following
        \x5F\x84\x40\x00\x05
        \x5F\x87\x00\x00\x00

        // returned when manually switched to HDMI input
        \x5F\x80\x40\x00\41\x00

        // get address
        \x45\x83

        */


        #endregion




        #region IBridgeAdvanced Members

        /// <summary>
        /// LinkToApi (bridge method)
        /// </summary>
        /// <param name="trilist"></param>
        /// <param name="joinStart"></param>
        /// <param name="joinMapKey"></param>
        /// <param name="bridge"></param>


        #endregion

        #region ICommunicationMonitor Members

        public StatusMonitorBase CommunicationMonitor { get; private set; }

        #endregion        


        /// <summary>
        /// Initialize 
        /// </summary>
        private void Init()
        {

            InitCommMonitor();
            StatusGet();
        }



        private void InitCommMonitor()
        {
            var pollInterval = _pollIntervalMs > 0 ? _pollIntervalMs : 30000;

            CommunicationMonitor = new GenericCommunicationMonitor(this, Communication, pollInterval, 180000, 300000,
                StatusGet);

            DeviceManager.AddDevice(CommunicationMonitor);

            StatusFeedback = new IntFeedback(() => (int)CommunicationMonitor.Status);

            CommunicationMonitor.StatusChange += (sender, args) =>
            {
                Debug.Console(2, this, "Device status: {0}", CommunicationMonitor.Status);
                StatusFeedback.FireUpdate();
            };
        }

        /// <summary>
        /// Custom activate
        /// </summary>
        /// <returns></returns>
        public override bool CustomActivate()
        {
            Communication.Connect();
            CommunicationMonitor.StatusChange +=
                (o, a) => Debug.Console(2, this, "Communication monitor state: {0}", CommunicationMonitor.Status);
            CommunicationMonitor.Start();
            return true;
        }

        /// <summary>
        /// Send text to device
        /// </summary>
        /// <param name="txt"></param>
        public void SendText(string txt)
        {
            this.LogDebug($"Sending text {ComTextHelper.GetEscapedText(txt)}");
            Communication.SendText(txt);
        }

        /// <summary>
        /// Communication bytes recieved
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e">Event args</param>
        private void Communication_BytesReceived(object sender, GenericCommMethodReceiveBytesArgs e)
        {
            try
            {
                this.LogDebug($"here are the bytes received {Encoding.UTF8.GetString(e.Bytes)}");
                ParseMessage(e.Bytes);
            }
            catch (Exception ex)
            {
                Debug.LogError(Debug.ErrorLogLevel.Warning, String.Format("Exception parsing feedback: {0}", ex.Message));
                Debug.LogError(Debug.ErrorLogLevel.Warning, String.Format("Stack trace: {0}", ex.StackTrace));
            }
        }

        private void ParseMessage(byte[] message)
        {

            this.LogDebug($"ParseMessage: {ComTextHelper.GetEscapedText(message)}");

            if (message[0] == 0x5f && message[1] == 0x72) //this signifies a power response
            {

                this.LogDebug("CEC Soundbar Power Feedback Received");
                byte powerByte = message[2];
                UpdatePowerFb(powerByte);
            }

        }


        /// <summary>
        /// Power feedback
        /// </summary>
        private void UpdatePowerFb(byte powerByte)
        {
            _powerIsOn = powerByte == 0x01 ? true : false;

            PowerIsOnFeedback.FireUpdate();
            this.LogInformation($"CEC Soundbar Feedback set {_powerIsOn}");

        }

        public void PowerToggle()
        {
            if (PowerIsOnFeedback.BoolValue)
            {
                PowerOff();
            }
            else if (!PowerIsOnFeedback.BoolValue)
            {
                PowerOn();
            }
        }

        /// <summary>
        /// </summary>
        public void StatusGet()
        {
            //Power Query
            SendText("\x45\x8F");
        }

        /// <summary>
        /// Power on (Cmd: 0x11) pdf page 42 
        /// Set: [HEADER=0xAA][Cmd=0x11][ID][DATA_LEN=0x01][DATA-1=0x01][CS=0x00]
        /// </summary>
        public void PowerOn()
        {
            _isPoweringOnIgnorePowerFb = true;
            Debug.Console(2, this, "CallingPowerOn");

            if (_powerOnUsesDiscreteCommand)
            {
                PowerOnDiscrete();
            }
            else
            {
                PowerOnOneTouch();
            }

            if (PowerIsOnFeedback.BoolValue)
            {
                return;
            }

        }

        /// <summary>
        /// Power off (Cmd: 0x11) pdf page 42 
        /// Set: [HEADER=0xAA][Cmd=0x11][ID][DATA_LEN=0x01][DATA-1=0x00][CS=0x00]
        /// </summary>
        public void PowerOff()
        {
            _isPoweringOnIgnorePowerFb = false;
            Debug.Console(2, this, "CallingPowerOff");
            // If a display has unreliable-power off feedback, just override this and
            // remove this check.

            SendText(PowerOffCmd);

            PowerIsOnFeedback.FireUpdate();
        }

        public void PowerOnDiscrete()
        {
            Debug.Console(2, this, "CallingPowerOnDiscrete");
            SendText(PowerOnDiscreteCmd);
        }


        public void PowerOnOneTouch()
        {
            Debug.Console(2, this, "CallingPowerOnOneTouch");
            SendText(PowerOnTv);
        }

        public void PowerOnArc()
        {
            Debug.Console(2, this, "CallingPowerOnArc");
            SendText(PowerOnArcCmd);
        }

        public void PowerOnOptical()
        {
            Debug.Console(2, this, "CallingPowerOnOptical");
            SendText(PowerOnOpticalCmd);
        }


        public void PowerOnHdmi()
        {
            Debug.Console(2, this, "CallingPowerOnHdmi");
            SendText(PowerOnHdmiCmd);
        }

        public void PowerOnInputX(string inputNumber)
        {
            Debug.Console(2, this, "CallingPOwerOnInputX");
            SendText(PowerOnInputXCmd.Replace("FF", inputNumber));
        }

        public void GetAddress()
        {
            Debug.Console(2, this, "CallingGetAddress");
            SendText(GetAddressCmd);
        }

        private void UpdateBooleanFeedback()
        {
            try
            {
                foreach (var item in InputFeedback)
                {
                    item.FireUpdate();
                }
            }
            catch (Exception e)
            {
                Debug.Console(0, this, "Exception Here - {0}", e.Message);
            }
        }



        #region IHasPowerControlWithFeedback Members

        public BoolFeedback PowerIsOnFeedback { get; private set; }




        #endregion


    }
}