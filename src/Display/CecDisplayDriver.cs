using Crestron.SimplSharp;
using Crestron.SimplSharpPro.DeviceSupport;
using Newtonsoft.Json;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using PepperDash.Essentials.Core.DeviceTypeInterfaces;
using PepperDash.Essentials.Devices.Common.Displays;
using System;
using System.Collections.Generic;
using System.Linq;
using Feedback = PepperDash.Essentials.Core.Feedback;

namespace PepperDash.Essentials.Plugin.Generic.Cec.Display
{
    public class CecDisplayDriverDisplayController : TwoWayDisplayBase, IHasInputs<string>, IBasicVolumeControls, ICommunicationMonitor,
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
        private string _powerOnCommand;
        private string _powerOffCommand;
        private string _powerStatusCommand;
        private readonly Dictionary<string, string> _inputCommandsByKey =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _powerOffRequiresInputCommand;
        private string _powerOffInputPreCommand;


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
                    _currentInputPort = (value > 0 && value <= InputPorts.Count) ? InputPorts[value - 1] : null;
                    CurrentInputFeedback.FireUpdate();
				InputNumberFeedback.FireUpdate();
				UpdateBooleanFeedback();
                    _selectableInputs.NotifyCurrentItemChanged();
			}
		}
		private int _CurrentInputNumber;

        private bool _isCoolingDown;
        private bool _isMuted;
        private bool _isPoweringOnIgnorePowerFb;
        private bool _isWarmingUp;
        private bool _lastCommandSentWasVolume;
        private int _lastVolumeSent;
        private string _lastInputCommandSent;
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
            _selectableInputs = new CecSelectableInputs(this);

            Id = _config.Id == null ? (byte) 0x01 : Convert.ToByte(_config.Id, 16);


            _upperLimit = _config.volumeUpperLimit;
            _lowerLimit = _config.volumeLowerLimit;
            _pollIntervalMs = _config.pollIntervalMs;
            _coolingTimeMs = _config.coolingTimeMs;
            _warmingTimeMs = _config.warmingTimeMs;

            ConfigureCommands();

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

        public ISelectableItems<string> Inputs
        {
            get { return _selectableInputs; }
        }

        private readonly CecSelectableInputs _selectableInputs;

        private class CecSelectableInputs : ISelectableItems<string>
        {
            private readonly CecDisplayDriverDisplayController _owner;

            public CecSelectableInputs(CecDisplayDriverDisplayController owner)
            {
                _owner = owner;
                Items = new Dictionary<string, ISelectableItem>(StringComparer.OrdinalIgnoreCase);
            }

            public Dictionary<string, ISelectableItem> Items { get; set; }

            public string CurrentItem
            {
                get;
                set;
            }

            public event EventHandler ItemsUpdated;
            public event EventHandler CurrentItemChanged;

            public void SetItems(IEnumerable<RoutingInputPort> ports)
            {
                var newItems = new Dictionary<string, ISelectableItem>(StringComparer.OrdinalIgnoreCase);

                foreach (var port in ports)
                {
                    newItems[port.Key] = new CecSelectableItem(
                        _owner,
                        port.Key,
                        GetInputDisplayName(port.Key)
                    );
                }

                Items = newItems;
                CurrentItem = _owner.GetCurrentInputKey();
                var handler = ItemsUpdated;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }

            public void NotifyCurrentItemChanged()
            {
                CurrentItem = _owner.GetCurrentInputKey();

                foreach (var item in Items.Values.OfType<CecSelectableItem>())
                {
                    item.NotifyItemUpdated();
                }

                var handler = CurrentItemChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        private class CecSelectableItem : ISelectableItem
        {
            private readonly CecDisplayDriverDisplayController _owner;

            public CecSelectableItem(CecDisplayDriverDisplayController owner, string key, string name)
            {
                _owner = owner;
                Key = key;
                Name = name;
            }

            public string Key { get; private set; }
            public string Name { get; private set; }

            public bool IsSelected { get; set; }

            public event EventHandler ItemUpdated;

            public void Select()
            {
                _owner.SelectInputByKey(Key);
            }

            public void NotifyItemUpdated()
            {
                IsSelected = string.Equals(_owner.GetCurrentInputKey(), Key, StringComparison.OrdinalIgnoreCase);

                var handler = ItemUpdated;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
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
            get { return () => _currentInputPort != null ? _currentInputPort.Key : string.Empty; }
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
        /// Power control on 
        /// </summary>
        public const string PowerControlOn = "\x40\x44\x6D";

        /// <summary>
        /// Power control off
        /// </summary>
		public const string PowerControlOff = "\x40\x36";

        public const string SamsungBePowerControlOn = "\x40\x04";
        public const string SamsungBeInputControlHdmi1 = "\x4F\x82\x10\x00";
        public const string SamsungBeInputControlHdmi2 = "\x4F\x82\x20\x00";
        public const string SamsungBeInputControlHdmi3 = "\x4F\x82\x30\x00";

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
        /// Input source control data1 - HDMI2
        /// </summary>
		public const string InputControlHdmi2 = "\x4F\x82\x20\x00";

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


        private void ConfigureCommands()
        {
            var profile = GetProfile(_config?.CecProfile);

            _powerOnCommand = ResolveCommandText(
                _config?.PowerOnCommandHex,
                profile.PowerOnHex,
                PowerControlOn
            );

            _powerOffCommand = ResolveCommandText(
                _config?.PowerOffCommandHex,
                profile.PowerOffHex,
                PowerControlOff
            );

            _powerStatusCommand = ResolveCommandText(
                _config?.PowerStatusCommandHex,
                profile.PowerStatusHex,
                PowerStatusCmd
            );

            var profileInputCommands = profile.InputHexByInputKey
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var configInputCommands = _config?.InputCommandsHexByInputKey
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            SetInputCommand("hdmiIn1", configInputCommands, profileInputCommands, InputControlHdmi1);
            SetInputCommand("hdmiIn2", configInputCommands, profileInputCommands, InputControlHdmi2);
            SetInputCommand("hdmiIn3", configInputCommands, profileInputCommands, InputControlHdmi3);
            SetInputCommand("hdmiIn4", configInputCommands, profileInputCommands, InputControlHdmi4);

            _powerOffRequiresInputCommand =
                _config?.PowerOffRequiresInputCommand
                ?? profile.PowerOffRequiresInputCommand;

            _powerOffInputPreCommand = ResolveCommandText(
                _config?.PowerOffInputPreCommandHex,
                profile.PowerOffInputPreCommandHex,
                null
            );
        }

        private void SetInputCommand(
            string inputKey,
            IDictionary<string, string> configInputCommands,
            IDictionary<string, string> profileInputCommands,
            string fallbackCommand
        )
        {
            string configHex;
            configInputCommands.TryGetValue(inputKey, out configHex);

            string profileHex;
            profileInputCommands.TryGetValue(inputKey, out profileHex);

            _inputCommandsByKey[inputKey] = ResolveCommandText(configHex, profileHex, fallbackCommand);
        }

        private static string ResolveCommandText(string configHex, string profileHex, string fallbackCommand)
        {
            string commandText;
            if (TryHexToCommandText(configHex, out commandText))
            {
                return commandText;
            }

            if (TryHexToCommandText(profileHex, out commandText))
            {
                return commandText;
            }

            return fallbackCommand;
        }

        private static bool TryHexToCommandText(string hex, out string commandText)
        {
            commandText = null;

            if (string.IsNullOrWhiteSpace(hex))
            {
                return false;
            }

            var parts = hex
                .Split(new[] { ' ', '\t', '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? p.Substring(2) : p)
                .ToArray();

            if (parts.Length == 0)
            {
                return false;
            }

            try
            {
                var bytes = parts.Select(p => Convert.ToByte(p, 16)).ToArray();
                commandText = new string(bytes.Select(b => (char)b).ToArray());
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static CecCommandProfile GetProfile(string profileName)
        {
            if (string.Equals(profileName, "samsungbe", StringComparison.OrdinalIgnoreCase))
            {
                return new CecCommandProfile
                {
                    PowerOnHex = "40 04",
                    PowerOffHex = "40 36",
                    PowerStatusHex = "40 8F",
                    PowerOffRequiresInputCommand = true,
                    InputHexByInputKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "hdmiIn1", "4F 82 10 00" },
                        { "hdmiIn2", "4F 82 20 00" },
                        { "hdmiIn3", "4F 82 30 00" }
                    }
                };
            }

            return new CecCommandProfile
            {
                PowerOnHex = "40 44 6D",
                PowerOffHex = "40 36",
                PowerStatusHex = "40 8F",
                PowerOffRequiresInputCommand = false,
                InputHexByInputKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "hdmiIn1", "4F 82 10 00" },
                    { "hdmiIn2", "4F 82 20 00" },
                    { "hdmiIn3", "4F 82 30 00" },
                    { "hdmiIn4", "4F 82 40 00" }
                }
            };
        }

        private sealed class CecCommandProfile
        {
            public string PowerOnHex { get; set; }
            public string PowerOffHex { get; set; }
            public string PowerStatusHex { get; set; }
            public Dictionary<string, string> InputHexByInputKey { get; set; }
            public bool PowerOffRequiresInputCommand { get; set; }
            public string PowerOffInputPreCommandHex { get; set; }
        }



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

            _selectableInputs.SetItems(InputPorts);

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

            ParsePowerStatusFromCec(message);
            ParseActiveSourceFromCec(message);

            // Handle power feedback if message has at least 3 bytes
            if (message.Length >= 3 && (message[2] == 0x01 || message[2] == 0x00))
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

        private void ParsePowerStatusFromCec(byte[] message)
        {
            // Report Power Status opcode is 0x90 with next byte status value
            for (var i = 0; i < message.Length - 1; i++)
            {
                if (message[i] != 0x90)
                {
                    continue;
                }

                var status = message[i + 1];

                switch (status)
                {
                    case 0x00: // On
                        UpdatePowerFb(0x01);
                        break;
                    case 0x01: // Standby
                        UpdatePowerFb(0x00);
                        break;
                    case 0x02: // In transition from Standby to On
                        _isWarmingUp = true;
                        IsWarmingUpFeedback.FireUpdate();
                        break;
                    case 0x03: // In transition from On to Standby
                        _isCoolingDown = true;
                        IsCoolingDownFeedback.FireUpdate();
                        break;
                }
            }
        }

        private void ParseActiveSourceFromCec(byte[] message)
        {
            // Active Source opcode is 0x82, followed by two-byte physical address
            for (var i = 0; i < message.Length - 2; i++)
            {
                if (message[i] != 0x82)
                {
                    continue;
                }

                var hi = message[i + 1];
                var lo = message[i + 2];

                // Typical source addresses mapped for this plugin: 1.0.0.0, 2.0.0.0, 3.0.0.0, 4.0.0.0
                if (lo != 0x00)
                {
                    continue;
                }

                var input = (hi >> 4);
                if (input >= 1 && input <= 4)
                {
                    CurrentInputNumber = input;
                    _powerIsOn = true;
                    PowerIsOnFeedback.FireUpdate();
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
        /// </summary>
        public void StatusGet()
        {
			   Communication.SendText(_powerStatusCommand ?? PowerStatusCmd);
            
        }

        /// <summary>
        /// Power on (Cmd: 0x11) pdf page 42 
        /// Set: [HEADER=0xAA][Cmd=0x11][ID][DATA_LEN=0x01][DATA-1=0x01][CS=0x00]
        /// </summary>
        public override void PowerOn()
        {
            _isPoweringOnIgnorePowerFb = true;
			Debug.Console(2, this, "CallingPowerOn");
            Communication.SendText(_powerOnCommand ?? PowerControlOn);

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
                if (_powerOffRequiresInputCommand)
                {
                    var inputPreCommand = _powerOffInputPreCommand;

                    if (string.IsNullOrEmpty(inputPreCommand))
                    {
                        if (!string.IsNullOrEmpty(_lastInputCommandSent))
                        {
                            inputPreCommand = _lastInputCommandSent;
                        }
                        else if (CurrentInputNumber > 0 && CurrentInputNumber <= InputPorts.Count)
                        {
                            var currentInputKey = InputPorts[CurrentInputNumber - 1].Key;
                            string currentInputCommand;
                            if (_inputCommandsByKey.TryGetValue(currentInputKey, out currentInputCommand))
                            {
                                inputPreCommand = currentInputCommand;
                            }
                        }
                        else
                        {
                            inputPreCommand = _inputCommandsByKey["hdmiIn1"];
                        }
                    }

                    if (!string.IsNullOrEmpty(inputPreCommand))
                    {
                        Communication.SendText(inputPreCommand);
                    }
                }

                Communication.SendText(_powerOffCommand ?? PowerControlOff);
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
            SendInputCommand("hdmiIn1", InputControlHdmi1);
        }

        /// <summary>

        /// </summary>
        public void InputHdmi2()
        {
			SendInputCommand("hdmiIn2", InputControlHdmi2);
        }

        /// <summary>

        /// </summary>
        public void InputHdmi3()
        {
			SendInputCommand("hdmiIn3", InputControlHdmi3);
        }

        /// <summary>

        /// </summary>
        public void InputHdmi4()
        {
            SendInputCommand("hdmiIn4", InputControlHdmi4);
        }

        private void SendInputCommand(string inputKey, string fallbackCommand)
        {
            string command;
            if (!_inputCommandsByKey.TryGetValue(inputKey, out command) || string.IsNullOrEmpty(command))
            {
                command = fallbackCommand;
            }

            _lastInputCommandSent = command;
            Communication.SendText(command);
        }

        private string GetCurrentInputKey()
        {
            if (CurrentInputNumber <= 0 || CurrentInputNumber > InputPorts.Count)
            {
                return string.Empty;
            }

            return InputPorts[CurrentInputNumber - 1].Key;
        }

        private void SelectInputByKey(string inputKey)
        {
            for (var i = 0; i < InputPorts.Count; i++)
            {
                if (string.Equals(InputPorts[i].Key, inputKey, StringComparison.OrdinalIgnoreCase))
                {
                    SetInput = i + 1;
                    return;
                }
            }
        }

        private static string GetInputDisplayName(string inputKey)
        {
            switch (inputKey)
            {
                case RoutingPortNames.HdmiIn1:
                    return "HDMI 1";
                case RoutingPortNames.HdmiIn2:
                    return "HDMI 2";
                case RoutingPortNames.HdmiIn3:
                    return "HDMI 3";
                case RoutingPortNames.HdmiIn4:
                    return "HDMI 4";
                default:
                    return inputKey;
            }
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