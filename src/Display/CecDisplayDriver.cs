using Crestron.SimplSharp;
using Crestron.SimplSharpPro.DeviceSupport;
using Newtonsoft.Json;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using PepperDash.Essentials.Devices.Common.Displays;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Feedback = PepperDash.Essentials.Core.Feedback;

namespace PepperDash.Essentials.Plugin.Generic.Cec.Display
{
    public class CecDisplayDriverDisplayController : TwoWayDisplayBase, IBasicVolumeControls, ICommunicationMonitor,
        IBridgeAdvanced
    {
        private enum PowerCommandSet
        {
            Default,
            SamsungUserControl
        }

        public const int InputPowerOn = 101;
        public const int InputPowerOff = 102;
        public static List<string> InputKeys = new List<string>();
        private readonly CecDisplayDriverPropertiesConfig _config;
        private readonly uint _coolingTimeMs;
        private readonly PowerCommandSet _powerCommandSet;
        private readonly bool _invertPowerFeedback;

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
        private CTimer _samsungPowerRetryTimer;
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
            _powerCommandSet = ParsePowerCommandSet(_config.PowerCommandSet);
            _invertPowerFeedback = _config.InvertPowerFeedback;

            Debug.Console(1, this, "Using CEC command set: {0}", _powerCommandSet);
            Debug.Console(1, this, "Invert power feedback: {0}", _invertPowerFeedback);
            Debug.Console(1, this, "CEC driver build marker: 2026-05-13b");

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
                if (value > 0 && value <= InputPorts.Count)
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
		/// QUERY_POWER_OSC	\x40\x8F
        /// </summary>
		public const string PowerStatusCmd = "\x40\x8F";

        /// <summary>
        /// Gets/sets the power state
        /// </summary>
		public const string PowerControlToggle = "\x40\x44\x6D";

        /// <summary>
        /// CEC Image View On
        /// </summary>
        public const string PowerControlImageViewOn = "\x40\x04";

        /// <summary>
        /// CEC Text View On
        /// </summary>
        public const string PowerControlTextViewOn = "\x40\x0D";

        /// <summary>
        /// Power control on 
        /// </summary>
        public const string PowerControlOn = "\x40\x44\x6D";

        /// <summary>
        /// Power control off
        /// </summary>
		public const string PowerControlOff = "\x40\x36";

        /// <summary>
        /// Power off using user control opcode
        /// </summary>
        public const string PowerControlOffUserControl = "\x40\x44\x6C";

        /// <summary>
        /// User Control Released
        /// </summary>
        public const string PowerControlUserControlRelease = "\x40\x45";

        /// <summary>
        /// Volume mute control data1 - on 
        /// </summary>
        public const byte VolumeMuteControlOn = 0x01;

        /// <summary>
        /// Volume mute control data1 - off
        /// </summary>
        public const byte VolumeMuteControlOff = 0x00;


		/*https://groups.io/g/crestron/topic/35798610
		 * https://support.crestron.com/app/answers/detail/a_id/5633/kw/CEC
		 * HDMI 1 \x4F\x82\x10\x00 tested
				HDMI 2 \x4F\x82\x20\x00 tested
				HDMI 3 \x4F\x82\x30\x00 tested
				HDMI 4 \x4F\x82\x40\x00 tested
				HDMI 5 \x4F\x82\x50\x00 not tested
				HDMI 6 \x4F\x82\x60\x00 not tested
		 */





        /// <summary>
        /// Input source control data1 - HDMI1
        /// </summary>
        public const string InputControlHdmi1 = "\x4F\x82\x10\x00";

        /// <summary>
        /// Input source control data1 - HDMI1 (Samsung user control set)
        /// </summary>
        public const string InputControlHdmi1SamsungUserControl = "\x4F\x82\x10\x00";

        /// <summary>
        /// Input source control data1 - HDMI2
        /// </summary>
		public const string InputControlHdmi2 = "\x4F\x82\x20\x00";

        /// <summary>
        /// Input source control data1 - HDMI2 (Samsung user control set)
        /// </summary>
		public const string InputControlHdmi2SamsungUserControl = "\x4F\x82\x20\x00";

        /// <summary>
        /// Input source control data1 - HDMI3
        /// </summary>
		public const string InputControlHdmi3 = "\x4F\x82\x30\x00";

        /// <summary>
        /// Input source control data1 - HDMI4
        /// </summary>
		public const string InputControlHdmi4 = "\x4F\x82\x40\x00";

        /// <summary>
        /// Input source control data1 - TV1
        /// </summary>
        public const byte InputControlTv1 = 0x40;





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
        /// </summary>
        public void MuteOff()
        {

        }

        /// <summary>
        /// </summary>
        public void MuteOn()
        {

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
            trilist.SetSigTrueAction(joinMap.PowerOff.JoinNumber, () =>
            {
                Debug.Console(1, this, "Bridge PowerOff join hit: {0}", joinMap.PowerOff.JoinNumber);
                PowerOff();
            });

            // PowerOn
            trilist.SetSigTrueAction(joinMap.PowerOn.JoinNumber, () =>
            {
                Debug.Console(1, this, "Bridge PowerOn join hit: {0}", joinMap.PowerOn.JoinNumber);
                PowerOn();
            });

            // Apply optional inversion to bridge feedback only. This does not alter command logic.
            if (_invertPowerFeedback)
            {
                PowerIsOnFeedback.LinkInputSig(trilist.BooleanInput[joinMap.PowerOff.JoinNumber]);
                PowerIsOnFeedback.LinkComplementInputSig(trilist.BooleanInput[joinMap.PowerOn.JoinNumber]);
            }
            else
            {
                PowerIsOnFeedback.LinkComplementInputSig(trilist.BooleanInput[joinMap.PowerOff.JoinNumber]);
                PowerIsOnFeedback.LinkInputSig(trilist.BooleanInput[joinMap.PowerOn.JoinNumber]);
            }

            // Input digitals
            var count = 0;

            foreach (var input in InputPorts)
            {
                var i = count;
                trilist.SetSigTrueAction((ushort) (joinMap.InputSelectOffset.JoinNumber + count),
                    () =>
                    {
                        Debug.Console(1, this, "Bridge Input join hit: {0} -> Input {1}", joinMap.InputSelectOffset.JoinNumber + (uint)i, i + 1);
                        SetInput = i + 1;
                    });

                trilist.StringInput[(ushort) (joinMap.InputNamesOffset.JoinNumber + count)].StringValue = input.Key;

                InputFeedback[count].LinkInputSig(
                    trilist.BooleanInput[joinMap.InputSelectOffset.JoinNumber + (uint) count]);
                count++;
            }

            // Some deployed bridge projects pulse standard display input joins (11,12,...) directly.
            // In samsungUserControl mode, force those joins to input actions to avoid misrouted
            // power-only behavior when external join-map data is stale or inconsistent.
            if (_powerCommandSet == PowerCommandSet.SamsungUserControl)
            {
                var standardInputJoinOffset = joinStart + 10;
                for (var i = 0; i < InputPorts.Count; i++)
                {
                    var index = i;
                    trilist.SetSigTrueAction((ushort)(standardInputJoinOffset + i), () =>
                    {
                        Debug.Console(1, this, "Samsung fallback input join {0} -> HDMI{1}", standardInputJoinOffset + index, index + 1);
                        SetInput = index + 1;
                    });
                }
            }


            // Input analog
            trilist.SetUShortSigAction(joinMap.InputSelect.JoinNumber, a =>
            {
                if (a == 0)
                {
                    PowerOff();
                }
                else if (a > 0 && a <= InputPorts.Count)
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
        private void AddRoutingInputPort(RoutingInputPort port)
        {
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
                    eRoutingPortConnectionType.Hdmi, new Action(InputHdmi1), this));

            AddRoutingInputPort(
                new RoutingInputPort(RoutingPortNames.HdmiIn2, eRoutingSignalType.Audio | eRoutingSignalType.Video,
                    eRoutingPortConnectionType.Hdmi, new Action(InputHdmi2), this));

            AddRoutingInputPort(
                new RoutingInputPort(RoutingPortNames.HdmiIn3, eRoutingSignalType.Audio | eRoutingSignalType.Video,
                    eRoutingPortConnectionType.Hdmi, new Action(InputHdmi3), this));

            AddRoutingInputPort(
                new RoutingInputPort(RoutingPortNames.HdmiIn4, eRoutingSignalType.Audio | eRoutingSignalType.Video,
                    eRoutingPortConnectionType.Hdmi, new Action(InputHdmi4), this));


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

                ParseMessage(newBytes);

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
            // Validate message is not null and has minimum length
            if (message == null || message.Length == 0)
            {
                Debug.Console(1, this, "Message is null or empty");
                return;
            }

            if (Debug.Level == 2)
            {
                Debug.Console(2, this, "ParseMessage received {0} bytes: {1}", message.Length, ComTextHelper.GetEscapedText(message));
            }

            // Handle CEC Report Power Status only:
            // [Header][0x90][Status]
            if (message.Length >= 3 && message[1] == 0x90)
            {
                byte powerByte = message[2];
                UpdatePowerFb(powerByte);
            }

            // Handle command byte if message has at least 6 bytes
            if (message.Length >= 6)
            {
                var command = message[5];

                switch (command)
                {
                    case 0x00:
                        {
                            // Handle command 0x00
                            break;
                        }
                    default:
                        {
                            if (Debug.Level >= 1)
                            {
                                Debug.Console(1, this, "Unknown command 0x{0:X2} in message: {1}", command, ComTextHelper.GetEscapedText(message));
                            }
                            break;
                        }
                }
            }
            else if (message.Length < 3)
            {
                // Log short messages for debugging
                Debug.Console(1, this, "Short message received ({0} bytes): {1}", message.Length, ComTextHelper.GetEscapedText(message));
            }
        }


        /// <summary>
        /// Power feedback
        /// </summary>
        private void UpdatePowerFb(byte powerByte)
        {
            // CEC Power Status values:
            // 0x00 = On, 0x01 = Standby, 0x02 = In transition Standby->On, 0x03 = In transition On->Standby
            bool? reportedPower = null;

            switch (powerByte)
            {
                case 0x00:
                    reportedPower = true;
                    break;
                case 0x01:
                    reportedPower = false;
                    break;
                case 0x02:
                case 0x03:
                    // Transitional states should not force local state changes.
                    return;
                default:
                    return;
            }

            var newVal = reportedPower.Value;

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
        /// </summary>
        public void StatusGet()
        {
			   Communication.SendText("\x40\x8F");
            
        }

        private static PowerCommandSet ParsePowerCommandSet(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return PowerCommandSet.Default;
            }

            if (value.Equals("samsungusercontrol", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("samsung-user-control", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("samsung_user_control", StringComparison.OrdinalIgnoreCase))
            {
                return PowerCommandSet.SamsungUserControl;
            }

            return PowerCommandSet.Default;
        }

        private string GetPowerOnCommand()
        {
            switch (_powerCommandSet)
            {
                case PowerCommandSet.SamsungUserControl:
                    return PowerControlOn;
                case PowerCommandSet.Default:
                default:
                    return PowerControlOn;
            }
        }

        private string GetPowerOffCommand()
        {
            switch (_powerCommandSet)
            {
                case PowerCommandSet.SamsungUserControl:
                    return PowerControlOffUserControl;
                case PowerCommandSet.Default:
                default:
                    return PowerControlOff;
            }
        }

        private void SendPowerOnCommand()
        {
            switch (_powerCommandSet)
            {
                case PowerCommandSet.SamsungUserControl:
                    SendSamsungUserControlPowerCommand(GetPowerOnCommand(), true);
                    return;
                case PowerCommandSet.Default:
                default:
                    Communication.SendText(GetPowerOnCommand());
                    return;
            }
        }

        private void SendPowerOffCommand()
        {
            switch (_powerCommandSet)
            {
                case PowerCommandSet.SamsungUserControl:
                    SendSamsungUserControlPowerOffSequence();
                    return;
                case PowerCommandSet.Default:
                default:
                    Communication.SendText(GetPowerOffCommand());
                    return;
            }
        }

        private void SendSamsungUserControlPowerOffSequence()
        {
            // Some Samsung displays respond to standby (0x36) while others respond
            // to user-control power off (0x44 0x6C). Send both, then key release.
            Communication.SendText(PowerControlOff);
            Communication.SendText(PowerControlOffUserControl);
            Communication.SendText(PowerControlUserControlRelease);

            _samsungPowerRetryTimer?.Stop();
            _samsungPowerRetryTimer = new CTimer(o =>
            {
                if (PowerIsOnFeedback.BoolValue)
                {
                    Communication.SendText(PowerControlOff);
                    Communication.SendText(PowerControlOffUserControl);
                    Communication.SendText(PowerControlUserControlRelease);
                }

                _samsungPowerRetryTimer?.Stop();
                _samsungPowerRetryTimer = null;
            }, null, 1500);
        }

        private void SendSamsungUserControlPowerCommand(string command, bool desiredPowerState)
        {
            Communication.SendText(command);

            if (desiredPowerState)
            {
                // Samsung wake-up is more reliable when user-control power on is followed by CEC view-on
                // opcodes.
                Communication.SendText(PowerControlImageViewOn);
                Communication.SendText(PowerControlTextViewOn);
            }

            _samsungPowerRetryTimer?.Stop();
            _samsungPowerRetryTimer = new CTimer(o =>
            {
                if (PowerIsOnFeedback.BoolValue != desiredPowerState)
                {
                    Communication.SendText(command);

                    if (desiredPowerState)
                    {
                        Communication.SendText(PowerControlImageViewOn);
                    }
                }

                _samsungPowerRetryTimer?.Stop();
                _samsungPowerRetryTimer = null;
            }, null, 1500);
        }

        private string GetInputHdmi1Command()
        {
            switch (_powerCommandSet)
            {
                case PowerCommandSet.SamsungUserControl:
                    return InputControlHdmi1SamsungUserControl;
                case PowerCommandSet.Default:
                default:
                    return InputControlHdmi1;
            }
        }

        private string GetInputHdmi2Command()
        {
            switch (_powerCommandSet)
            {
                case PowerCommandSet.SamsungUserControl:
                    return InputControlHdmi2SamsungUserControl;
                case PowerCommandSet.Default:
                default:
                    return InputControlHdmi2;
            }
        }

        private string GetInputHdmi3Command()
        {
            return InputControlHdmi3;
        }

        private string GetInputHdmi4Command()
        {
            return InputControlHdmi4;
        }

        /// <summary>
        /// Power on (Cmd: 0x11) pdf page 42 
        /// Set: [HEADER=0xAA][Cmd=0x11][ID][DATA_LEN=0x01][DATA-1=0x01][CS=0x00]
        /// </summary>
        public override void PowerOn()
        {
            _isPoweringOnIgnorePowerFb = true;
			Debug.Console(2, this, "CallingPowerOn");

            if (_powerCommandSet == PowerCommandSet.SamsungUserControl)
            {
                // Samsung CEC power behavior is inconsistent; always transmit requested command.
                SendPowerOnCommand();
                return;
            }

            SendPowerOnCommand();

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

            if (_powerCommandSet == PowerCommandSet.SamsungUserControl)
            {
                // Samsung CEC power behavior is inconsistent; always transmit requested command.
                SendPowerOffCommand();
                return;
            }

            // If a display has unreliable-power off feedback, just override this and
            // remove this check.
            if (!_isWarmingUp && !_isCoolingDown) // PowerIsOnFeedback.BoolValue &&
            {
				SendPowerOffCommand();
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
        public void InputHdmi1()
        {
            var command = GetInputHdmi1Command();
            Debug.Console(1, this, "InputHdmi1 command bytes: {0}", ToLogEscapedHex(command));
            Communication.SendText(command);
        }

        /// <summary>

        /// </summary>
        public void InputHdmi2()
        {
			var command = GetInputHdmi2Command();
			Debug.Console(1, this, "InputHdmi2 command bytes: {0}", ToLogEscapedHex(command));
			Communication.SendText(command);
        }

        private static string ToLogEscapedHex(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return "<empty>";
            }

            var sb = new StringBuilder();
            foreach (var c in command)
            {
                sb.AppendFormat("[{0:X2}]", (byte)c);
            }

            return sb.ToString();
        }

        /// <summary>

        /// </summary>
        public void InputHdmi3()
        {
			Communication.SendText(GetInputHdmi3Command());
        }

        /// <summary>

        /// </summary>
        public void InputHdmi4()
        {
			Communication.SendText(GetInputHdmi4Command());
        }

 







        /// <summary>
        /// Executes a switch, turning on display if necessary.
        /// </summary>
        /// <param name="selector"></param>
        public override void ExecuteSwitch(object selector)
        {
            //if (!(selector is Action))
            //    Debug.Console(1, this, "WARNING: ExecuteSwitch cannot handle type {0}", selector.GetType());

            var action = selector as Action;
            if (action == null)
            {
                return;
            }

            // In samsungUserControl mode, input selection should not implicitly trigger
            // power commands. Keep input routing independent from power control.
            if (_powerCommandSet == PowerCommandSet.SamsungUserControl)
            {
                action();
                return;
            }

            if (_powerIsOn)
            {
                action();
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
                    action();
                };
                IsWarmingUpFeedback.OutputChange += handler; // attach and wait for on FB
                PowerOn();
            }
        }



    }
}