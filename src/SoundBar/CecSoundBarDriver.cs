using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Crestron.SimplSharp;
using PepperDash.Core;
using PepperDash.Core.Logging;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Generic.Cec.SoundBar
{
    public class CecSoundBarController : EssentialsDevice, ICommunicationMonitor, IHasPowerControlWithFeedback
    {
        public StatusMonitorBase CommunicationMonitor { get; private set; }
        public IBasicCommunication Communication { get; private set; }
        public IntFeedback StatusFeedback { get; set; }

        private int addressChangeCounter = 0;
        public byte Id { get; private set; }

        private List<int> PhysicalAddressBytes;

        private byte[] _physicalAddress = { 0x00, 0x00, 0x00, 0x00 };

        private bool physicalAddressSetinConfig = false;

        public byte[] PhysicalAddress
        {
            get { return _physicalAddress; }
            private set
            {
                SetPhysicalAddress(value);
            }
        }

        private void SetPhysicalAddress(byte[] value)
        {
            if(physicalAddressSetinConfig)
            {
                this.LogDebug("Setting Physical Address from config");
                _physicalAddress = value;
                this.LogDebug($"Physical Address set in config {ComTextHelper.GetEscapedText(value)}");
                return;
            }
            // If the value is not set in config, we get it from the device
            if (value == null || value.Length != 4)
            {
                this.LogDebug($"Physical Address not as expected {ComTextHelper.GetEscapedText(value)}");
                addressChangeCounter = 0;
            }
            else if (!value.SequenceEqual(_physicalAddress))
            {
                addressChangeCounter += 1;
                if (addressChangeCounter > 2)
                {
                    _physicalAddress = value;
                    this.LogDebug($"Physical Address changed {ComTextHelper.GetEscapedText(value)}");
                    addressChangeCounter = 0;
                }
                else
                {
                    this.LogDebug($"Physical Address not yet changed {ComTextHelper.GetEscapedText(value)}, tracker is at {addressChangeCounter}, it will update when greater than 2.");
                }
            }
        }






        /*public const string PowerOffCmd = "\x1F\x36";
        public const string PowerOnCmd = "\x4F\x82\x10\x00";        
        public const string PowerOnTv = "\x4F\x82\x10\x00";        
        public const string PowerOnArcCmd = "\x4F\x82\x11\x00";
        public const string PowerOnOpticalCmd = "\x4F\x82\x12\x00";*/
        public static readonly byte[] PowerOffCmd = { 0x1F, 0x36 };
        public static readonly byte[] PowerOnCmd = { 0x4F, 0x82, 0x10, 0x00 };
        public static readonly byte[] PowerOnTv = { 0x4F, 0x82, 0x10, 0x00 };
        public static readonly byte[] PowerOnArcCmd = { 0x4F, 0x82, 0x11, 0x00 };
        public static readonly byte[] PowerOnOpticalCmd = { 0x4F, 0x82, 0x12, 0x00 };
        public static readonly byte[] GetPowerStatus = { 0x45, 0x8F };
        public static readonly byte[] GetDestinationID = { 0x45, 0x83 };


        // Tested with JBL Boost 2.1 
        // CEC-O-Matic > source: Playback 1 with Physical Address 3.1.0.0
        //  - End-user features > System Audio Control > System Audio Mode request
        // CEC-O-Matic > destinaiton: Audio System
        //public const string PowerOnHdmiCmd = "\x45\x70\x31\x00"; //stop this from being a constant

        public byte[] PowerOnHdmiCmd()
        {
            byte[] powerOnHdmiCmd = new byte[6]
            {
                0x45, // Header
                0x70, // Command
                0x00, // Physical Address 1
                0x00, // Physical Address 2
                0x00, // Physical Address 3
                0x00  // Physical Address 4
            };
            
            if(PhysicalAddress!= null && !PhysicalAddress.SequenceEqual(new byte[4] { 0x00, 0x00, 0x00, 0x00 })) //if a physical address isn't null and doesn't have the default value
            {
                Array.Copy(PhysicalAddress, 0, powerOnHdmiCmd, 2, 4);
            }
            else
            {
                this.LogDebug($"PowerOnHdmiCmd: Physical Address not set");
            }
            return powerOnHdmiCmd;
        }

        

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

        public BoolFeedback PowerIsOnFeedback { get; private set; }

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

            PhysicalAddressBytes = _config.physicalAddress;
            if (PhysicalAddressBytes != null && PhysicalAddressBytes.Count == 4)
            {
                physicalAddressSetinConfig = true;
                this.LogDebug($"Physical Address set in config");
                PhysicalAddress = PhysicalAddressBytes.Select(b => Convert.ToByte(b)).ToArray();
            }
            else
            {

                this.LogDebug($"Physical Address not set");
                foreach(var item in PhysicalAddressBytes)
                {
                    this.LogDebug($"Physical Address item {item}");
                }
            }


            PowerIsOnFeedback = new BoolFeedback(() => _powerIsOn);

            Init();
        }

        
        /*public override void Initialize()
        {
            Communication.Connect();
            CommunicationMonitor.Start();

            var pollInterval = _pollIntervalMs > 0 ? _pollIntervalMs : 30000;

            CommunicationMonitor = new GenericCommunicationMonitor(this, Communication, pollInterval, 180000, 300000,
                StatusGet);

            DeviceManager.AddDevice(CommunicationMonitor);

            StatusFeedback = new IntFeedback(() => (int)CommunicationMonitor.Status);

            CommunicationMonitor.StatusChange += (sender, args) =>
            {
                //Debug.Console(2, this, "Device status: {0}", CommunicationMonitor.Status);
                
                StatusFeedback.FireUpdate();
            };

            StatusGet();

            AddressGet();

            base.Initialize();
        }

        /// <summary>
        /// Custom activate
        /// </summary>
        /// <returns></returns>
        /// */
        
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
        /*public void SendText(string txt)
        {
            this.LogDebug($"Sending text {txt}");
            Communication.SendText(txt);
        }*/

        public void SendBytes(byte[] bytes)
        {
            this.LogDebug($"Sending bytes {ComTextHelper.GetEscapedText(bytes)}");
            Communication.SendBytes(bytes);
        }

        public void SendCecOMaticCommand(string txt)
        {            
            var hexBytes = txt.Split(':').Select(b => Convert.ToByte(b, 16)).ToArray();            
            // SendText(Encoding.UTF8.GetString(hexBytes));
            //SendText(Encoding.GetEncoding(28591).GetString(hexBytes));
            SendBytes(hexBytes);
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

            else if (message[1] == 0x84)
            {
                this.LogDebug("CEC Soundbar Address Feedback Received");
                if (!physicalAddressSetinConfig)
                {             
                byte[] addressBytes = new byte[4];
                Array.Copy(message, 2, addressBytes, 0, 4);
                PhysicalAddress = addressBytes;
                this.LogDebug($"Physical Address set to {ComTextHelper.GetEscapedText(PhysicalAddress)}");
                }
            }

            else if (message[1] == 0x90)
            {
                this.LogDebug("CEC Soundbar Input Feedback Received");
                byte inputByte = message[2];
                CurrentInputNumber = inputByte;
            }

        }

        private void Init()
        {
            Debug.Console(2, this, "Initializing CEC Soundbar");
            InitCommMonitor();
        }

        private void InitCommMonitor()
        {
            var pollInterval = _pollIntervalMs > 0 ? _pollIntervalMs : 30000;

            CommunicationMonitor = new GenericCommunicationMonitor(this, Communication, pollInterval, 180000, 300000,
                AddressGet);

            DeviceManager.AddDevice(CommunicationMonitor);

            StatusFeedback = new IntFeedback(() => (int)CommunicationMonitor.Status);

            CommunicationMonitor.StatusChange += (sender, args) =>
            {
                Debug.Console(2, this, "Device status: {0}", CommunicationMonitor.Status);
                StatusFeedback.FireUpdate();
            };
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
            //SendText("\x45\x8F");
            SendBytes(GetPowerStatus);
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
                this.LogInformation($"PhysicalAddress == {Encoding.GetEncoding(28591).GetString(PhysicalAddress)}");
            }
            else
            {
                SendBytes(PowerOnCmd);
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

            //SendText(PowerOffCmd);
            SendBytes(PowerOffCmd);

            PowerIsOnFeedback.FireUpdate();
        }

        
        public void PowerOnDiscrete()
        {
            Debug.Console(2, this, "CallingPowerOnDiscrete");
            //SendText(PowerOnHdmiCmd());
            SendBytes(PowerOnHdmiCmd());
        }

        
        public void AddressGet()
        {
            Debug.Console(2, this, "CallingGetAddress");
            //SendText("\x45\x83");
            SendBytes(GetDestinationID);
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
    }
}