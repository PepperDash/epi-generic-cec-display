// For Basic SIMPL# Classes
// For Basic SIMPL#Pro classes

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro.DeviceSupport;
using Newtonsoft.Json;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using PepperDash.Essentials.Core.DeviceInfo;
using PepperDash.Essentials.Core.Routing;
using Feedback = PepperDash.Essentials.Core.Feedback;

namespace PepperDash.Plugin.Display.CecDisplayDriver
{
    public class CecDisplayDriverDisplayController : TwoWayDisplayBase, IBasicVolumeControls, ICommunicationMonitor,
        IBridgeAdvanced
    {
        public const int InputPowerOn = 101;
        public const int InputPowerOff = 102;
        public static List<string> InputKeys = new List<string>();
        private readonly CecDisplayDriverPropertiesConfig _config;
        private readonly uint _coolingTimeMs;

        private readonly int _lowerLimit;
        private readonly long _pollIntervalMs;
        private readonly int _upperLimit;
        private readonly uint _warmingTimeMs;


        public List<BoolFeedback> InputFeedback;

        
        private RoutingInputPort _currentInputPort;
        
        private byte[] _incomingBuffer = {};

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

        private bool _isCoolingDown;
        private bool _isMuted;
        private bool _isPoweringOnIgnorePowerFb;
        private bool _isWarmingUp;
        private bool _lastCommandSentWasVolume;
        private int _lastVolumeSent;
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
        public CecDisplayDriverDisplayController(string key, string name, CecDisplayDriverPropertiesConfig config,
            IBasicCommunication comms)
            : base(key, name)
        {
            Communication = comms;
            Communication.BytesReceived += Communication_BytesReceived;
            _config = config;

            Id = _config.Id == null ? (byte) 0x01 : Convert.ToByte(_config.Id, 16);


            _upperLimit = _config.volumeUpperLimit;
            _lowerLimit = _config.volumeLowerLimit;
            _pollIntervalMs = _config.pollIntervalMs;
            _coolingTimeMs = _config.coolingTimeMs;
            _warmingTimeMs = _config.warmingTimeMs;

            Init();
        }

        public IBasicCommunication Communication { get; private set; }
        public byte Id { get; private set; }
        public IntFeedback StatusFeedback { get; set; }

        public int SetInput
        {
			get { return CurrentInputNumber; }
            set 
			{
				if (value > 0 && value < InputPorts.Count)
				{
					ExecuteSwitch(InputPorts.ElementAt(value - 1).Selector);
					CurrentInputNumber = value;
				}
			}
        }


        protected override Func<bool> PowerIsOnFeedbackFunc
        {
            get { return () => _powerIsOn; }
        }

        protected override Func<bool> IsCoolingDownFeedbackFunc
        {
            get { return () => _isCoolingDown; }
        }

        protected override Func<bool> IsWarmingUpFeedbackFunc
        {
            get { return () => _isWarmingUp; }
        }

        protected override Func<string> CurrentInputFeedbackFunc
        {
            get { return () => _currentInputPort.Key; }
        }

        /// <summary>
        /// 
        /// </summary>
        public override FeedbackCollection<Feedback> Feedbacks
        {
            get
            {
                var list = base.Feedbacks;
                list.AddRange(new List<Feedback>
                {
                    VolumeLevelFeedback,
                    MuteFeedback,
                    CurrentInputFeedback
                });
                return list;
            }
        }

        #region Command Constants

        /// <summary>
        /// Header byte
        /// </summary>
        // public const byte Header = 0x40;

        /// <summary>
		/// QUERY_POWER_OSC	\x40\x8F
        /// </summary>
		public const string PowerStatusCmd = "\x40\x8F";

        /// Display status control (Cmd: 0x0D) pdf page 34
        /// Gets the display status, status includes: val1=Lamp, val2=Temperature, val3=Bright_Sensor, val4=No_Sync, val5=Current_Temp, val6=Fan
        /// </summary>
        public const byte DisplayStatusControlCmd = 0x0D;

        /// <summary>
        /// Power control (Cmd: 0x11) pdf page 42
        /// Gets/sets the power state
        /// </summary>
		public const string PowerControlToggle = "\x40\x44\x6D";

        /// <summary>
        /// Power control data1 - on 
        /// </summary>
        public const string PowerControlOn = "\x40\x44\x6D";

        /// <summary>
        /// Power control data1 - off
        /// </summary>
		public const string PowerControlOff = "\x40\x36";

        /// <summary>
        /// Volume level control (Cmd: 0x12) pdf page 44
        /// Gets/sets the volume level
        /// Level range 0d - 100d (0x00 - 0x64)
        /// </summary>
        public const byte VolumeLevelControlCmd = 0x12;

        /// <summary>
        /// Volume mute control (Cmd: 0x13) pdf page 45
        /// Gets/sets the volume mute state
        /// </summary>
        public const byte VolumeMuteControlCmd = 0x13;

        /// <summary>
        /// Volume mute control data1 - on 
        /// </summary>
        public const byte VolumeMuteControlOn = 0x01;

        /// <summary>
        /// Volume mute control data1 - off
        /// </summary>
        public const byte VolumeMuteControlOff = 0x00;

        /// <summary>
        /// Input source control (Cmd: 0x14) pdf page 46
        /// Gets/sets the input state
        /// </summary>
        public const byte InputControlCmd = 0x14;

        /// <summary>
        /// Input source control data1 - S-Video1
        /// </summary>
        public const byte InputControlSvideo1 = 0x04;

        /// <summary>
        /// Input source control data1 - Component1
        /// </summary>
        public const byte InputControlComponent1 = 0x08;

        /// <summary>
        /// Input source control data1 - AV1
        /// </summary>
        public const byte InputControlAv1 = 0x0C;

        /// <summary>
        /// Input source control data1 - AV2
        /// </summary>
        public const byte InputControlAv2 = 0x0D;

        /// <summary>
        /// Input source control data1 - Scart1
        /// </summary>
        public const byte InputControlScart1 = 0x0E;

        /// <summary>
        /// Input source control data1 - DVI1
        /// </summary>
        public const byte InputControlDvi1 = 0x18;

        /// <summary>
        /// Input source control data1 - PC1
        /// </summary>
        public const byte InputControlPc1 = 0x14;

        /// <summary>
        /// Input source control data1 - BNC1
        /// </summary>
        public const byte InputControlBnc1 = 0x1E;

        /// <summary>
        /// Input source control data1 - DVI Video1
        /// </summary>
        public const byte InputControlDviVideo1 = 0x1F;

        /// <summary>
        /// Input source control data1 - HDMI1
        /// </summary>
        public const byte InputControlHdmi1 = 0x21;

        /// <summary>
        /// Input source control data1 - HDMI1 PC
        /// </summary>
        public const byte InputControlHdmi1Pc = 0x22;

        /// <summary>
        /// Input source control data1 - HDMI2
        /// </summary>
        public const byte InputControlHdmi2 = 0x23;

        /// <summary>
        /// Input source control data1 - HDMI2 PC
        /// </summary>
        public const byte InputControlHdmi2Pc = 0x24;

        /// <summary>
        /// Input source control data1 - DisplayPort1
        /// </summary>
        public const byte InputControlDisplayPort1 = 0x25;

        /// <summary>
        /// Input source control data1 - DisplayPort2
        /// </summary>
        public const byte InputControlDisplayPort2 = 0x26;

        /// <summary>
        /// Input source control data1 - DisplayPort3
        /// </summary>
        public const byte InputControlDisplayPort3 = 0x27;

        /// <summary>
        /// Input source control data1 - HDMI3
        /// </summary>
        public const byte InputControlHdmi3 = 0x31;

        /// <summary>
        /// Input source control data1 - HDMI3 PC
        /// </summary>
        public const byte InputControlHdmi3Pc = 0x32;

        /// <summary>
        /// Input source control data1 - HDMI4
        /// </summary>
        public const byte InputControlHdmi4 = 0x33;

        /// <summary>
        /// Input source control data1 - HDMI4 PC
        /// </summary>
        public const byte InputControlHdmi4Pc = 0x34;

        /// <summary>
        /// Input source control data1 - TV1
        /// </summary>
        public const byte InputControlTv1 = 0x40;

        /// <summary>
        /// Input source control data1 - HDBase-T1
        /// </summary>
        public const byte InputControlHdBaseT1 = 0x55;



        /// <summary>
        /// Volume increment/decrement control (Cmd: 0x62) pdf page 122
        /// Set only, increments/decrements the volume level
        /// </summary>
        public const byte VolumeAdjustCmd = 0x62;

        /// <summary>
        /// Volume increment/decrement control data1 - up
        /// </summary>
        public const byte VolumeAdjustUp = 0x00;

        /// <summary>
        /// Volume increment/decrement control data1 - down
        /// </summary>
        public const byte VolumeAdjustDown = 0x01;

        /// <summary>
        /// Virtual remote control (Cmd: 0xB0) pdf pg. 81
        /// Set only, emulates the IR remote
        /// </summary>
        public const byte VirtualRemoteCmd = 0xB0;

        /// <summary>
        /// Virtual remote control data1 (keyCode) - Menu (0x1A)
        /// </summary>
        public const byte VirtualRemoteMenu = 0x1A;

        /// <summary>
        /// Virtual remote control data1 (keyCode) - Dpad Up (0x60)
        /// </summary>
        public const byte VirtualRemoteUp = 0x60;

        /// <summary>
        /// Virtual remote control data1 (keyCode) - Dpad Down (0x61)
        /// </summary>
        public const byte VirtualRemoteDown = 0x61;

        /// <summary>
        /// Virtual remote control data1 (keyCode) - Dpad Left (0x65)
        /// </summary>
        public const byte VirtualRemoteLeft = 0x65;

        /// <summary>
        /// Virtual remote control data1 (keyCode) - Dpad Right (0x62)
        /// </summary>
        public const byte VirtualRemoteRight = 0x62;

        /// <summary>
        /// Virtual remote control data1 (keyCode) - Dpad Selct (0x68)
        /// </summary>
        public const byte VirtualRemoteSelect = 0x68;

        /// <summary>
        /// Virtual remote control data1 (keyCode) - Exit (0x2D)
        /// </summary>
        public const byte VirtualRemoteExit = 0x2D;



        #endregion

        #region IBasicVolumeWithFeedback Members



        /// <summary>
        /// Volume level feedback property
        /// </summary>
        public IntFeedback VolumeLevelFeedback { get; private set; }

        /// <summary>
        /// volume mte feedback property
        /// </summary>
        public BoolFeedback MuteFeedback { get; private set; }

        /// <summary>
        /// Mute off (Cmd: 0x13) pdf page 45
        /// Set: [Header=0xAA][Cmd=0x13][ID][DATA_LEN=0x01][DATA-1=0x00][CS=0x00]
        /// </summary>
        public void MuteOff()
        {
            SendBytes(new byte[] {VolumeMuteControlCmd});
        }

        /// <summary>
        /// Mute on (Cmd: 0x13) pdf page 45
        /// Set: [Header=0xAA][Cmd=0x13][ID][DATA_LEN=0x01][DATA-1=0x01][CS=0x00]
        /// </summary>
        public void MuteOn()
        {
            SendBytes(new byte[] {VolumeMuteControlCmd});
        }

        /// <summary>
        /// Mute toggle
        /// </summary>
        public void MuteToggle()
        {
            if (_isMuted)
            {
                MuteOff();
            }
            else
            {
                MuteOn();
            }
        }

        /// <summary>
        /// Volume down (decrement)
        /// </summary>
        /// <param name="pressRelease"></param>
        public void VolumeDown(bool pressRelease)
        {
            if (pressRelease)
            {
                // _volumeIncrementer.StartDown();
                
            }
            else
            {
                // _volumeIncrementer.Stop();
            }
        }

        /// <summary>
        /// Volume up (increment)
        /// </summary>
        /// <param name="pressRelease"></param>
        public void VolumeUp(bool pressRelease)
        {
            if (pressRelease)
            {
                
            }
            else
            {

            }
        }

        #endregion

        #region IBridgeAdvanced Members

        /// <summary>
        /// LinkToApi (bridge method)
        /// </summary>
        /// <param name="trilist"></param>
        /// <param name="joinStart"></param>
        /// <param name="joinMapKey"></param>
        /// <param name="bridge"></param>
        public void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
        {
            var joinMap = new CecDisplayDriverControllerJoinMap(joinStart);

            var joinMapSerialized = JoinMapHelper.GetSerializedJoinMapForDevice(joinMapKey);

            if (!string.IsNullOrEmpty(joinMapSerialized))
            {
                joinMap = JsonConvert.DeserializeObject<CecDisplayDriverControllerJoinMap>(joinMapSerialized);
            }

            Debug.Console(1, "Linking to Trilist '{0}'", trilist.ID.ToString("X"));
            Debug.Console(0, "Linking to Display: {0}", Name);

            trilist.StringInput[joinMap.Name.JoinNumber].StringValue = Name;

            CommunicationMonitor.IsOnlineFeedback.LinkInputSig(trilist.BooleanInput[joinMap.IsOnline.JoinNumber]);
            

            // input analog feedback
            InputNumberFeedback.LinkInputSig(trilist.UShortInput[joinMap.InputSelect.JoinNumber]);


            // Power Off
            trilist.SetSigTrueAction(joinMap.PowerOff.JoinNumber, () => PowerOff());

            PowerIsOnFeedback.LinkComplementInputSig(trilist.BooleanInput[joinMap.PowerOff.JoinNumber]);

            // PowerOn
            trilist.SetSigTrueAction(joinMap.PowerOn.JoinNumber, PowerOn);
            PowerIsOnFeedback.LinkInputSig(trilist.BooleanInput[joinMap.PowerOn.JoinNumber]);

            // Input digitals
            var count = 0;

            foreach (var input in InputPorts)
            {
                var i = count;
                trilist.SetSigTrueAction((ushort) (joinMap.InputSelectOffset.JoinNumber + count),
					() => SetInput = i + 1);

                trilist.StringInput[(ushort) (joinMap.InputNamesOffset.JoinNumber + count)].StringValue = input.Key;

                InputFeedback[count].LinkInputSig(
                    trilist.BooleanInput[joinMap.InputSelectOffset.JoinNumber + (uint) count]);
                count++;
            }


            // Input analog
            trilist.SetUShortSigAction(joinMap.InputSelect.JoinNumber, a =>
            {
                if (a == 0)
                {
                    PowerOff();
                }
                else if (a > 0 && a < InputPorts.Count)
                {
                    SetInput = a;
					
                }
                else if (a == 102)
                {
                    PowerToggle();
                }
                Debug.Console(2, this, "InputChange {0}", a);
            });

            // Volume


            trilist.SetBoolSigAction(joinMap.VolumeUp.JoinNumber, VolumeUp);


            trilist.SetBoolSigAction(joinMap.VolumeDown.JoinNumber, VolumeDown);


            trilist.SetSigTrueAction(joinMap.VolumeMute.JoinNumber, MuteToggle);

            trilist.SetSigTrueAction(joinMap.VolumeMuteOn.JoinNumber, MuteOn);
            trilist.SetSigTrueAction(joinMap.VolumeMuteOff.JoinNumber, MuteOff);


        }

        #endregion

        #region ICommunicationMonitor Members

        public StatusMonitorBase CommunicationMonitor { get; private set; }

        #endregion

        //public static void LoadPlugin()
        //{
        //    DeviceFactory.AddFactoryForType("samsungmdcplugin", BuildDevice);
        //}

        //public static SamsungMdcDisplayController BuildDevice(DeviceConfig dc)
        //{
        //    //var config = JsonConvert.DeserializeObject<DeviceConfig>(dc.Properties.ToString());
        //    var newMe = new SamsungMdcDisplayController(dc);
        //    return newMe;
        //}

        /// <summary>
        /// Add routing input port 
        /// </summary>
        /// <param name="port"></param>
        /// <param name="fbMatch"></param>
        private void AddRoutingInputPort(RoutingInputPort port, byte fbMatch)
        {
            port.FeedbackMatchObject = fbMatch;
            InputPorts.Add(port);
        }

        /// <summary>
        /// Initialize 
        /// </summary>
        private void Init()
        {
            WarmupTime = _warmingTimeMs > 0 ? _warmingTimeMs : 10000;
            CooldownTime = _coolingTimeMs > 0 ? _coolingTimeMs : 8000;

            InitCommMonitor();

            InitInputPortsAndFeedbacks();

            StatusGet();
        }



        private void InitCommMonitor()
        {
            var pollInterval = _pollIntervalMs > 0 ? _pollIntervalMs : 30000;

            CommunicationMonitor = new GenericCommunicationMonitor(this, Communication, pollInterval, 180000, 300000,
                StatusGet);

            DeviceManager.AddDevice(CommunicationMonitor);

            StatusFeedback = new IntFeedback(() => (int) CommunicationMonitor.Status);

            CommunicationMonitor.StatusChange += (sender, args) =>
            {
                Debug.Console(2, this, "Device status: {0}", CommunicationMonitor.Status);
                StatusFeedback.FireUpdate();
            };
        }

        private void InitInputPortsAndFeedbacks()
        {
            //_InputFeedback = new List<bool>();
            InputFeedback = new List<BoolFeedback>();

            AddRoutingInputPort(
                new RoutingInputPort(RoutingPortNames.HdmiIn1, eRoutingSignalType.Audio | eRoutingSignalType.Video,
                    eRoutingPortConnectionType.Hdmi, new Action(InputHdmi1), this), InputControlHdmi1);

            AddRoutingInputPort(
                new RoutingInputPort(RoutingPortNames.HdmiIn2, eRoutingSignalType.Audio | eRoutingSignalType.Video,
                    eRoutingPortConnectionType.Hdmi, new Action(InputHdmi2), this), InputControlHdmi2);

            AddRoutingInputPort(
                new RoutingInputPort(RoutingPortNames.HdmiIn3, eRoutingSignalType.Audio | eRoutingSignalType.Video,
                    eRoutingPortConnectionType.Hdmi, new Action(InputHdmi3), this), InputControlHdmi3);

            AddRoutingInputPort(
                new RoutingInputPort(RoutingPortNames.HdmiIn4, eRoutingSignalType.Audio | eRoutingSignalType.Video,
                    eRoutingPortConnectionType.Hdmi, new Action(InputHdmi4), this), InputControlHdmi4);

            AddRoutingInputPort(
                new RoutingInputPort(RoutingPortNames.DisplayPortIn1,
                    eRoutingSignalType.Audio | eRoutingSignalType.Video,
                    eRoutingPortConnectionType.DisplayPort, new Action(InputDisplayPort1), this),
                InputControlDisplayPort1);

            AddRoutingInputPort(
                new RoutingInputPort(RoutingPortNames.DisplayPortIn2,
                    eRoutingSignalType.Audio | eRoutingSignalType.Video,
                    eRoutingPortConnectionType.DisplayPort, new Action(InputDisplayPort2), this),
                InputControlDisplayPort2);

            AddRoutingInputPort(
                new RoutingInputPort(RoutingPortNames.DviIn, eRoutingSignalType.Audio | eRoutingSignalType.Video,
                    eRoutingPortConnectionType.Dvi, new Action(InputDvi1), this), InputControlDvi1);


            for (var i = 0; i < InputPorts.Count; i++)
            {
                var j = i;

				InputFeedback.Add(new BoolFeedback(() => CurrentInputNumber == j + 1));
            }

            InputNumberFeedback = new IntFeedback(() =>
            {
                //Debug.Console(2, this, "Change Input number {0}", _inputNumber);
				return CurrentInputNumber;
            });
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
        /// Communication bytes recieved
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e">Event args</param>
        private void Communication_BytesReceived(object sender, GenericCommMethodReceiveBytesArgs e)
        {
            try
            {
                //Debug.Console(2, this, "Received from e:{0}", ComTextHelper.GetEscapedText(e.Bytes));

                // Append the incoming bytes with whatever is in the buffer
                var newBytes = new byte[_incomingBuffer.Length + e.Bytes.Length];
                _incomingBuffer.CopyTo(newBytes, 0);
                e.Bytes.CopyTo(newBytes, _incomingBuffer.Length);

                // clear buffer
                //_incomingBuffer = _incomingBuffer.Skip(_incomingBuffer.Length).ToArray();

                if (Debug.Level == 2)
                {
                    // This check is here to prevent
                    // following string format from building unnecessarily on level 0 or 1
                    Debug.Console(2, this, "Received new bytes:{0}", ComTextHelper.GetEscapedText(newBytes));
                }


            }
            catch (Exception ex)
            {
                Debug.LogError(Debug.ErrorLogLevel.Warning, String.Format("Exception parsing feedback: {0}", ex.Message));
                Debug.LogError(Debug.ErrorLogLevel.Warning, String.Format("Stack trace: {0}", ex.StackTrace));
            }
        }

        private void ParseMessage(byte[] message)
        {
            var command = message[5];

            if (Debug.Level == 2)
            {
                // This check is here to prevent following string format from building unnecessarily on level 0 or 1
                Debug.Console(2, this, "Add to buffer:{0}", ComTextHelper.GetEscapedText(_incomingBuffer));
            }

            switch (command)
            {
              
                case 0x00:
                {
                    
                    break;
                }
                 



                default:
                {
                    Debug.Console(1, this, "Unknown message: {0}", ComTextHelper.GetEscapedText(message));
                    break;
                }
            }
        }


        /// <summary>
        /// Power feedback
        /// </summary>
        private void UpdatePowerFb(byte powerByte)
        {
            var newVal = powerByte == 1;
			if (!newVal)
			{
				CurrentInputNumber = 0;
			}
			if (newVal == _powerIsOn)
            {
                return;
            }
            _powerIsOn = newVal;

            PowerIsOnFeedback.FireUpdate();
        }

        // <summary>
        // Volume feedback
        // </summary>


        /// <summary>
        /// Mute feedback
        /// </summary>
        private void UpdateMuteFb(byte b)
        {
            var newMute = b == 1;

            if (newMute == _isMuted)
            {
                return;
            }
            _isMuted = newMute;
            MuteFeedback.FireUpdate();
        }

        /// <summary>
        /// Input feedback
        /// </summary>
        private void UpdateInputFb(byte b)
        {
            var newInput = InputPorts.FirstOrDefault(i => i.FeedbackMatchObject.Equals(b));
            if (newInput != null && _powerIsOn)
            {
                _currentInputPort = newInput;
                CurrentInputFeedback.FireUpdate();
                var key = newInput.Key;
                switch (key)
                {
                    case "hdmiIn1":
						CurrentInputNumber = 1;
                        break;
                    case "hdmiIn2":
						CurrentInputNumber = 2;
                        break;
                    case "hdmiIn3":
						CurrentInputNumber = 3;
                        break;
                    case "hdmiIn4":
						CurrentInputNumber = 4;
                        break;
                    case "displayPortIn1":
						CurrentInputNumber = 5;
                        break;
                    case "displayPortIn2":
						CurrentInputNumber = 6;
                        break;
                    case "dviIn":
						CurrentInputNumber = 7;
                        break;
                }
				InputNumberFeedback.FireUpdate();
            }

            
            
        }

        /// <summary>
        /// Formats an outgoing message. 
        /// Third byte will be replaced with ID and last byte will be replaced with calculated checksum.
        /// All bytes to make a valid message must be included and can be represented with 0x00. 
        /// Get ex. [HEADER][CMD][ID][DATA_LEN][CS]
        /// Set ex. [HEADER][CMD][ID][DATA_LEN][DATA-1...DATA-N][CS]
        /// </summary>
        /// <param name="b">byte array</param>
        private void SendBytes(byte[] b)
        {
            // Command structure 
            // [HEADER][CMD][ID][DATA_LEN][DATA-1]....[DATA-N][CHK_SUM]
            // PowerOn ex: 0xAA,0x11,0x01,0x01,0x01,0x01
            if (_lastCommandSentWasVolume) // If the last command sent was volume
            {
                if (b[1] != 0x12) // Check if this command is volume, and if not, delay this command 
                {
                    CrestronEnvironment.Sleep(100);
                }
            }

            b[2] = Id;
            // append checksum by adding all bytes, except last which should be 00
            var checksum = 0;
            for (var i = 1; i < b.Length - 1; i++) // add 2nd through 2nd-to-last bytes
            {
                checksum += b[i];
            }
            checksum = checksum & 0x000000FF; // mask off MSBs
            b[b.Length - 1] = (byte) checksum;

            _lastCommandSentWasVolume = b[1] == 0x12;

            Communication.SendBytes(b);
        }

        /// <summary>
        /// Status control (Cmd: 0x00) pdf page 26
        /// Get: [HEADER=0xAA][Cmd=0x00][ID][DATA_LEN=0x00][CS=0x00]
        /// </summary>
        public void StatusGet()
        {
            //SendBytes(new byte[] { Header, StatusControlCmd, 0x00, 0x00, StatusControlGet, 0x00 });
            SendBytes(new byte[] {0x00, 0x00, 0x00, 0x00});
            /*
            PowerGet();
            _pollRing = null;
            _pollRing = new CTimer(o => InputGet(), null, 1000);
            */
        }

        /// <summary>
        /// Power on (Cmd: 0x11) pdf page 42 
        /// Set: [HEADER=0xAA][Cmd=0x11][ID][DATA_LEN=0x01][DATA-1=0x01][CS=0x00]
        /// </summary>
        public override void PowerOn()
        {
            _isPoweringOnIgnorePowerFb = true;
			Debug.Console(2, this, "CallingPowerOn");
            Communication.SendText(PowerControlOn);

            if (PowerIsOnFeedback.BoolValue || _isWarmingUp || _isCoolingDown)
            {
                return;
            }
            _isWarmingUp = true;
            IsWarmingUpFeedback.FireUpdate();
            // Fake power-up cycle
            WarmupTimer = new CTimer(o =>
            {
                _isWarmingUp = false;
                _powerIsOn = true;
                IsWarmingUpFeedback.FireUpdate();
                PowerIsOnFeedback.FireUpdate();
            }, WarmupTime);
        }

        /// <summary>
        /// Power off (Cmd: 0x11) pdf page 42 
        /// Set: [HEADER=0xAA][Cmd=0x11][ID][DATA_LEN=0x01][DATA-1=0x00][CS=0x00]
        /// </summary>
        public override void PowerOff()
        {
            _isPoweringOnIgnorePowerFb = false;
			Debug.Console(2, this, "CallingPowerOff");
            // If a display has unreliable-power off feedback, just override this and
            // remove this check.
            if (!_isWarmingUp && !_isCoolingDown) // PowerIsOnFeedback.BoolValue &&
            {
				Communication.SendText(PowerControlOff);
                _isCoolingDown = true;
                _powerIsOn = false;
				CurrentInputNumber = 0;
                
                InputNumberFeedback.FireUpdate();
                PowerIsOnFeedback.FireUpdate();
                IsCoolingDownFeedback.FireUpdate();
                // Fake cool-down cycle
                CooldownTimer = new CTimer(o =>
                {
                    _isCoolingDown = false;
                    IsCoolingDownFeedback.FireUpdate();
                }, CooldownTime);
            }
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


        /// <summary>		
        /// Power toggle (Cmd: 0x11) pdf page 42 
        /// Set: [HEADER=0xAA][Cmd=0x11][ID][DATA_LEN=0x01][DATA-1=0x01||0x00][CS=0x00]
        /// </summary>
        public override void PowerToggle()
        {
            if (PowerIsOnFeedback.BoolValue && !IsWarmingUpFeedback.BoolValue)
            {
                PowerOff();
            }
            else if (!PowerIsOnFeedback.BoolValue && !IsCoolingDownFeedback.BoolValue)
            {
                PowerOn();
            }
        }

        /// <summary>
        /// Input HDMI 1 (Cmd: 0x14) pdf page 426
        /// Set: [HEADER=0xAA][Cmd=0x14][ID][DATA_LEN=0x01][DATA-1=0x21][CS=0x00]
        /// </summary>
        public void InputHdmi1()
        {
            SendBytes(new byte[] {InputControlCmd});
        }

        /// <summary>
        /// Input HDMI 2 (Cmd: 0x14) pdf page 426
        /// Set: [HEADER=0xAA][Cmd=0x14][ID][DATA_LEN=0x01][DATA-1=0x23][CS=0x00]
        /// </summary>
        public void InputHdmi2()
        {
            SendBytes(new byte[] {InputControlCmd});
        }

        /// <summary>
        /// Input HDMI 3 (Cmd: 0x14) pdf page 426
        /// Set: [HEADER=0xAA][Cmd=0x14][ID][DATA_LEN=0x01][DATA-1=0x31][CS=0x00]
        /// </summary>
        public void InputHdmi3()
        {
            SendBytes(new byte[] {InputControlCmd});
        }

        /// <summary>
        /// Input HDMI 4 (Cmd: 0x14) pdf page 426
        /// Set: [HEADER=0xAA][Cmd=0x14][ID][DATA_LEN=0x01][DATA-1=0x33][CS=0x00]
        /// </summary>
        public void InputHdmi4()
        {
            SendBytes(new byte[] {InputControlCmd});
        }

        /// <summary>
        /// Input DisplayPort 1 (Cmd: 0x14) pdf page 426
        /// Set: [HEADER=0xAA][Cmd=0x14][ID][DATA_LEN=0x01][DATA-1=0x25][CS=0x00]
        /// </summary>
        public void InputDisplayPort1()
        {
            SendBytes(new byte[] {InputControlCmd});
        }

        /// <summary>
        /// Input DisplayPort 2 (Cmd: 0x14) pdf page 426
        /// Set: [HEADER=0xAA][Cmd=0x14][ID][DATA_LEN=0x01][DATA-1=0x26][CS=0x00]
        /// </summary>
        public void InputDisplayPort2()
        {
            SendBytes(new byte[] {InputControlCmd});
        }

        /// <summary>
        /// Input DVI 1 (Cmd: 0x14) pdf page 426
        /// Set: [HEADER=0xAA][Cmd=0x14][ID][DATA_LEN=0x01][DATA-1=0x18][CS=0x00]
        /// </summary>
        public void InputDvi1()
        {
            SendBytes(new byte[] {InputControlCmd});
        }







        /// <summary>
        /// Executes a switch, turning on display if necessary.
        /// </summary>
        /// <param name="selector"></param>
        public override void ExecuteSwitch(object selector)
        {
            //if (!(selector is Action))
            //    Debug.Console(1, this, "WARNING: ExecuteSwitch cannot handle type {0}", selector.GetType());

            if (_powerIsOn)
            {
                var action = selector as Action;
                if (action != null)
                {
                    action();
                }
            }
            else // if power is off, wait until we get on FB to send it. 
            {
                // One-time event handler to wait for power on before executing switch
                EventHandler<FeedbackEventArgs> handler = null; // necessary to allow reference inside lambda to handler
                handler = (o, a) =>
                {
                    if (_isWarmingUp)
                    {
                        return;
                    }

                    IsWarmingUpFeedback.OutputChange -= handler;
                    var action = selector as Action;
                    if (action != null)
                    {
                        action();
                    }
                };
                IsWarmingUpFeedback.OutputChange += handler; // attach and wait for on FB
                PowerOn();
            }
        }



    }
}