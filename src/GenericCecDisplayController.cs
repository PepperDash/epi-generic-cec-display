using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using PepperDash.Essentials.Core.Routing;
using PepperDash.Essentials.Devices.Displays;

namespace GenericCecDisplay
{
	public class GenericCecDisplayController : TwoWayDisplayBase, IBridgeAdvanced, ICommunicationMonitor, 
		IInputHdmi1, IInputHdmi2, IInputHdmi3, IInputHdmi4, IBasicVolumeControls
	{
		/// <summary>
		/// Constructor for IBaseCommunication
		/// </summary>
		/// <param name="name"></param>
		/// <param name="config"></param>
		/// <param name="key"></param>
		/// <param name="comms"></param>		
		public GenericCecDisplayController(string key, string name, GenericCecDisplayPropertiesConfig config, IBasicCommunication comms)
			: base(key, name)
		{
			ResetDebugLevels();

			_cecPowerSet = config.CecPowerSet > 0 
				? config.CecPowerSet 
				: 1;

			Communication = comms;
			Communication.BytesReceived += Communication_BytesReceived;

			var pollIntervalMs = config.PollIntervalMs > 45000 ? config.PollIntervalMs : 45000;
			CommunicationMonitor = new GenericCommunicationMonitor(this, Communication, pollIntervalMs, 180000, 300000, PowerGet);
			CommunicationMonitor.StatusChange += CommunicationMonitor_StatusChange;

			CooldownTime = config.CoolingTimeMs == 0 ? 10000 : config.CoolingTimeMs;
			WarmupTime = config.WarmingTimeMs == 0 ? 10000 : config.WarmingTimeMs;

			InitializeRoutingPorts();
		}


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
			var joinMap = new GenericCecDisplayJoinMap(joinStart);

			// This adds the join map to the collection on the bridge
			if (bridge != null)
			{
				bridge.AddJoinMap(Key, joinMap);
			}

			var customJoins = JoinMapHelper.TryGetJoinMapAdvancedForDevice(joinMapKey);
			if (customJoins != null)
			{
				joinMap.SetCustomJoinData(customJoins);
			}

			Debug.Console(DebugNotice, "Linking to Trilist '{0}'", trilist.ID.ToString("X"));
			Debug.Console(DebugTrace, "Linking to Bridge Type {0}", GetType().Name);

			// links to bridge
			// device name
			trilist.SetString(joinMap.Name.JoinNumber, Name);

			//var twoWayDisplay = this as TwoWayDisplayBase;
			//trilist.SetBool(joinMap.IsTwoWayDisplay.JoinNumber, twoWayDisplay != null);

			if (CommunicationMonitor != null)
			{
				CommunicationMonitor.IsOnlineFeedback.LinkInputSig(trilist.BooleanInput[joinMap.IsOnline.JoinNumber]);
			}

			// Power Off
			trilist.SetSigTrueAction(joinMap.PowerOff.JoinNumber, PowerOff);
			PowerIsOnFeedback.LinkComplementInputSig(trilist.BooleanInput[joinMap.PowerOff.JoinNumber]);


			// PowerOn
			trilist.SetSigTrueAction(joinMap.PowerOn.JoinNumber, PowerOn);
			PowerIsOnFeedback.LinkInputSig(trilist.BooleanInput[joinMap.PowerOn.JoinNumber]);


			// input (digital select, digital feedback, names)
			for (var i = 0; i < InputPorts.Count; i++)
			{
				var inputIndex = i;
				var input = InputPorts.ElementAt(inputIndex);

				if (input == null) continue;

				var inputSelectJoin = (uint)(joinMap.InputSelectOffset.JoinNumber + inputIndex);
				var inputNameJoin = (uint)(joinMap.InputNamesOffset.JoinNumber + inputIndex);

				trilist.SetSigTrueAction(inputSelectJoin, () =>
				{
					Debug.Console(DebugVerbose, this, "INputSelect Digital-'{0}'", inputIndex + 1);
					SetInput = inputIndex + 1;
				});

				trilist.StringInput[inputNameJoin].StringValue = string.IsNullOrEmpty(input.Key)
					? string.Empty
					: input.Key;

				InputFeedback[inputIndex].LinkInputSig(trilist.BooleanInput[inputSelectJoin]);
			}

			// input (analog select)
			trilist.SetUShortSigAction(joinMap.InputSelect.JoinNumber, analogValue =>
			{
				Debug.Console(DebugVerbose, this, "InpoutSelect Analog-'{0}'", analogValue);
				SetInput = analogValue;
			});

			// input (analog feedback)
			if (CurrentInputFeedback != null)
				CurrentInputNumberFeedback.LinkInputSig(trilist.UShortInput[joinMap.InputSelect.JoinNumber]);

			if (CurrentInputFeedback != null)
				CurrentInputFeedback.OutputChange +=
					(sender, args) => Debug.Console(DebugVerbose, this, "CurrentInputFeedback: {0}", args.StringValue);

			// volume
			trilist.SetBoolSigAction(joinMap.VolumeUp.JoinNumber, VolumeUp);
			trilist.SetBoolSigAction(joinMap.VolumeDown.JoinNumber, VolumeDown);

			// mute
			trilist.SetSigTrueAction(joinMap.VolumeMute.JoinNumber, MuteToggle);
			trilist.SetSigTrueAction(joinMap.VolumeMuteOn.JoinNumber, MuteOn);
			trilist.SetSigTrueAction(joinMap.VolumeMuteOff.JoinNumber, MuteOff);

			// bridge online change
			trilist.OnlineStatusChange += (sender, args) =>
			{
				if (!args.DeviceOnLine) return;

				// device name
				trilist.SetString(joinMap.Name.JoinNumber, Name);

				PowerIsOnFeedback.FireUpdate();

				if (CurrentInputFeedback != null)
					CurrentInputFeedback.FireUpdate();

				if (CurrentInputNumberFeedback != null)
					CurrentInputNumberFeedback.FireUpdate();

				for (var i = 0; i < InputPorts.Count; i++)
				{
					var inputIndex = i;
					if (InputFeedback != null)
						InputFeedback[inputIndex].FireUpdate();
				}
			};
		}

		#endregion


		#region ICommunicationMonitor Members

		// incoming byte buffer
		private readonly byte[] _incomingBuffer = { };

		/// <summary>
		/// IBasicComminication object
		/// </summary>
		public IBasicCommunication Communication { get; private set; }

		/// <summary>
		/// Communication status monitor object
		/// </summary>
		public StatusMonitorBase CommunicationMonitor { get; private set; }

		// communicationMonitor status change
		private void CommunicationMonitor_StatusChange(object sender, MonitorStatusChangeEventArgs args)
		{
			CommunicationMonitor.IsOnlineFeedback.FireUpdate();
		}

		// Communication bytes recieved
		private void Communication_BytesReceived(object sender, GenericCommMethodReceiveBytesArgs e)
		{
			try
			{
				// following string format from building unnecessarily on level 0 or 1
				if (Debug.Level == DebugVerbose)
					Debug.Console(DebugVerbose, this, "Communication_BytesReceived: '{0}'", ComTextHelper.GetEscapedText(e.Bytes));

				// Append the incoming bytes with whatever is in the buffer
				var newBytes = new byte[_incomingBuffer.Length + e.Bytes.Length];
				_incomingBuffer.CopyTo(newBytes, 0);
				e.Bytes.CopyTo(newBytes, _incomingBuffer.Length);

				// following string format from building unnecessarily on level 0 or 1
				if (Debug.Level == DebugVerbose)
					Debug.Console(DebugVerbose, this, "Communication_BytesReceived: new bytes-'{0}'", ComTextHelper.GetEscapedText(newBytes));

				ProcessResponse(newBytes);
			}
			catch (Exception ex)
			{
				Debug.LogError(Debug.ErrorLogLevel.Warning, String.Format("Communication_BytesReceived Exception Message: {0}", ex.Message));
				Debug.LogError(Debug.ErrorLogLevel.Warning, String.Format("Communication_BytesReceived Exception Stacktrace: {0}", ex.StackTrace));
				if (ex.InnerException != null) Debug.LogError(Debug.ErrorLogLevel.Warning, String.Format("Communication_BytesReceived InnerException: {0}", ex.InnerException));
			}
		}

		// processes device responses
		private void ProcessResponse(byte[] message)
		{
			// following string format from building unnecessarily on level 0 or 1
			if (Debug.Level == DebugVerbose)
				Debug.Console(DebugVerbose, this, "ProcessResponse: '{0}'", ComTextHelper.GetEscapedText(message));


			if (message.Length < 3)
			{
				Debug.Console(DebugVerbose, this, "ProcessResponse: message.Length '{0}' is less than required", message.Length);
				return;
			}

			// power response prefix, message[2] == state
			if (message[0] != 0x04 && message[1] != 0x90)
			{
				Debug.Console(DebugVerbose, this, "ProcessResponse: unhandled response '{0}'", ComTextHelper.GetEscapedText(message));
				return;
			}
			switch (message[2])
			{
					// power on
				case 0x00:
				{
					Debug.Console(DebugVerbose, this, "ProcessResponse: message[2] '{0}' = power on", message[2]);
					PowerIsOn = true;
					break;
				}
					// power off
				case 0x01:
				{
					Debug.Console(DebugVerbose, this, "ProcessResponse: message[2] '{0}' = power off", message[2]);
					PowerIsOn = false;
					break;
				}
					// warming
				case 0x02:
				{
					Debug.Console(DebugVerbose, this, "ProcessResponse: message[2] '{0}' = warming", message[2]);
					break;
				}
					// cooling
				case 0x03:
				{
					Debug.Console(DebugVerbose, this, "ProcessResponse: message[2] '{0}' = cooling", message[2]);
					break;
				}
				default:
				{
					Debug.Console(DebugVerbose, this, "ProcessResponse: unhandled power response '{0}'", message[2]);
					break;
				}
			}
		}

		/// <summary>
		/// send ASCII formatted commands 
		/// </summary>
		/// <param name="cmd"></param>
		public void SendText(string cmd)
		{
			if (string.IsNullOrEmpty(cmd))
			{
				Debug.Console(DebugNotice, this, "SendText: cmd is null or empty, verify cmd");
				return;
			}

			Debug.Console(DebugVerbose, this, "SendText: '{0}'", ComTextHelper.GetEscapedText(cmd));

			if (!Communication.IsConnected)
			{
				Communication.Connect();
			}

			Communication.SendText(cmd);
		}


		/// <summary>
		/// Send byte formatted commands
		/// </summary>
		/// <param name="cmd"></param>
		public void SendBytes(byte[] cmd)
		{
			if (cmd == null || cmd.Length == 0)
			{
				Debug.Console(DebugNotice, this, "SendBytes: cmd is null or empty, verify cmd");
				return;
			}

			Debug.Console(DebugVerbose, this, "SendBytes: '{0}'", ComTextHelper.GetEscapedText(cmd));

			if (!Communication.IsConnected)
			{
				Communication.Connect();
			}

			Communication.SendBytes(cmd);
		}

		#endregion


		/// <summary>
		/// Initialize
		/// </summary>
		public override void Initialize()
		{
			Communication.Connect();
			CommunicationMonitor.Start();
		}


		/// <summary>
		/// Executes a switch, turning on display if necesary
		/// </summary>
		/// <param name="selector"></param>
		public override void ExecuteSwitch(object selector)
		{
			Debug.Console(DebugNotice, this, "ExecuteSwitch: selector '{0}'", selector);

			if (PowerIsOn)
			{
				Debug.Console(DebugNotice, this, "ExecuteSwitch: if - PowerIsOn {0}", PowerIsOn);

				var action = selector as Action;
				Debug.Console(DebugNotice, this, "ExecuteSwitch: action is {0}", action == null ? "null" : "not null");
				if (action != null)
					CrestronInvoke.BeginInvoke(o => action());
			}
			// if power is off, wait until we get on FB to send it
			else
			{
				Debug.Console(DebugNotice, this, "ExecuteSwitch: else - PowerIsOn {0}", PowerIsOn);

				// one-time event handler to wait for power on before executing switch 
				EventHandler<FeedbackEventArgs> handler = null; // necessary to allow reference inside lambda to handler
				handler = (sender, args) =>
				{
					if (IsWarmingUp) return;

					IsWarmingUpFeedback.OutputChange -= handler;

					var action = selector as Action;
					Debug.Console(DebugNotice, this, "ExecuteSwitch: action is {0}", action == null ? "null" : "not null");
					if (action != null)
						CrestronInvoke.BeginInvoke(o => action());
				};
				IsWarmingUpFeedback.OutputChange += handler; // attach and wait for on fb
				PowerOn();
			}

			PowerGet();
		}

		#region Power

		private bool _isCoolingDown;
		private bool _isWarmingUp;
		private bool _powerIsOn;

		private uint _cecPowerSet;

		/// <summary>
		/// Power is on property
		/// </summary>
		public bool PowerIsOn
		{
			get
			{
				Debug.Console(DebugVerbose, this, "PowerIsOn: {0}", _powerIsOn); 
				return _powerIsOn;
			}
			set
			{
				if (_powerIsOn == value)
				{
					return;
				}

				_powerIsOn = value;
				Debug.Console(DebugVerbose, this, "PowerIsOn: {0}", _powerIsOn);
				PowerIsOnFeedback.FireUpdate();				
			}
		}

		/// <summary>
		/// Is warming property
		/// </summary>
		public bool IsWarmingUp
		{
			get
			{
				Debug.Console(DebugVerbose, this, "IsWarmingUp: {0}", _isWarmingUp);
				return _isWarmingUp;
			}
			set
			{
				_isWarmingUp = value;
				IsWarmingUpFeedback.FireUpdate();

				if (_isWarmingUp)
				{
					WarmupTimer = new CTimer(t =>
					{
						_isWarmingUp = false;
						IsWarmingUpFeedback.FireUpdate();
						PowerGet();
					}, WarmupTime);
				}

				Debug.Console(DebugVerbose, this, "IsWarmingUp: {0}", _isWarmingUp);
			}
		}

		/// <summary>
		/// Is cooling property
		/// </summary>
		public bool IsCoolingDown
		{
			get
			{
				Debug.Console(DebugVerbose, this, "IsCoolingDown: {0}", _isCoolingDown); 
				return _isCoolingDown;
			}
			set
			{
				_isCoolingDown = value;
				IsCoolingDownFeedback.FireUpdate();

				if (_isCoolingDown)
				{
					CooldownTimer = new CTimer(t =>
					{
						_isCoolingDown = false;
						IsCoolingDownFeedback.FireUpdate();
						PowerGet();
					}, CooldownTime);
				}

				Debug.Console(DebugVerbose, this, "IsCoolingDown: {0}", _isCoolingDown);
			}
		}

		protected override Func<bool> PowerIsOnFeedbackFunc
		{
			get { return () => PowerIsOn; }
		}

		protected override Func<bool> IsCoolingDownFeedbackFunc
		{
			get { return () => IsCoolingDown; }
		}

		protected override Func<bool> IsWarmingUpFeedbackFunc
		{
			get { return () => IsWarmingUp; }
		}

		/// <summary>
		/// Set Power On For Device
		/// </summary>
		public override void PowerOn()
		{
			Debug.Console(DebugVerbose, this, "PowerOn: PowerIsOn {0} | _cecPowerSet {1}", PowerIsOn, _cecPowerSet);

			if (IsWarmingUp || IsCoolingDown)
			{
				Debug.Console(DebugVerbose, this, "PowerOn: IsWarmingUp {0} || IsCoolingDown {1} > return", IsWarmingUp,
					IsCoolingDown);
				return;
			}

			if (!PowerIsOn) IsWarmingUp = true;

			var cmd = _cecPowerSet == 1
				? CecCommands.PowerOnCec1
				: CecCommands.PowerOnCec2;

			SendBytes(cmd);
		}

		/// <summary>
		/// Set Power Off for Device
		/// </summary>
		public override void PowerOff()
		{
			Debug.Console(DebugVerbose, this, "PowerOff: PowerIsOn {0} | _cecPowerSet {1}", PowerIsOn, _cecPowerSet);

			if (IsWarmingUp || IsCoolingDown)
			{
				Debug.Console(DebugVerbose, this, "PowerOff: IsWarmingUp {0} || IsCoolingDown {1} > return", IsWarmingUp,
					IsCoolingDown);
				return;
			}

			if (PowerIsOn) IsCoolingDown = true;

			var cmd = _cecPowerSet == 1
				? CecCommands.PowerOffCec1
				: CecCommands.PowerOffCec2;

			SendBytes(cmd);
		}

		/// <summary>
		/// Poll Power
		/// </summary>
		public void PowerGet()
		{
			SendBytes(CecCommands.PowerStatus);
		}

		/// <summary>
		/// Changes cec power commands, currently supports 1 || 2
		/// </summary>
		/// <param name="set"></param>
		public void PowerCecSet(uint set)
		{
			if (set == 0 || set > 2)
			{
				Debug.Console(DebugVerbose, this, "PowerCecSet: _cecPowerSet {0}", _cecPowerSet);
				return;
			}

			_cecPowerSet = set;

			Debug.Console(DebugVerbose, this, "PowerCecSet: _cecPowerSet {0}", _cecPowerSet);
		}


		/// <summary>
		/// Toggle current power state for device
		/// </summary>
		public override void PowerToggle()
		{
			Debug.Console(DebugVerbose, this, "PowerToggle: PowerIsOn {0}", PowerIsOn);

			if (PowerIsOn)
			{
				PowerOff();
			}
			else
			{
				PowerOn();
			}
		}

		#endregion




		#region Inputs


		/// <summary>
		/// Input power on constant
		/// </summary>
		public const int InputPowerOn = 101;

		/// <summary>
		/// Input power off constant
		/// </summary>
		public const int InputPowerOff = 102;

		/// <summary>
		/// Input key list
		/// </summary>
		public static List<string> InputKeys = new List<string>();

		/// <summary>
		/// Input (digital) feedback
		/// </summary>
		public List<BoolFeedback> InputFeedback;

		/// <summary>
		/// Input number (analog) feedback
		/// </summary>
		public IntFeedback CurrentInputNumberFeedback;

		private RoutingInputPort _currentInputPort;

		protected override Func<string> CurrentInputFeedbackFunc
		{
			get { return () => _currentInputPort != null ? _currentInputPort.Key : string.Empty; }
		}

		private List<bool> _inputFeedback;
		private int _currentInputNumber;

		/// <summary>
		/// Input number property
		/// </summary>
		public int CurrentInputNumber
		{
			get { return _currentInputNumber; }
			private set
			{
				_currentInputNumber = value;
				CurrentInputNumberFeedback.FireUpdate();
				UpdateInputBooleanFeedback();
			}
		}

		/// <summary>
		/// Sets the requested input
		/// </summary>
		public int SetInput
		{
			set
			{
				if (value <= 0 || value > InputPorts.Count)
				{
					Debug.Console(DebugNotice, this, "SetInput: value-'{0}' is out of range (1 - {1})", value, InputPorts.Count);
					return;
				}

				Debug.Console(DebugNotice, this, "SetInput: value-'{0}'", value);

				// -1 to get actual input in list after 0d check
				var port = GetInputPort(value - 1);
				if (port == null)
				{
					Debug.Console(DebugNotice, this, "SetInput: failed to get input port");
					return;
				}

				Debug.Console(DebugVerbose, this, "SetInput: port.key-'{0}', port.Selector-'{1}', port.ConnectionType-'{2}', port.FeebackMatchObject-'{3}'",
					port.Key, port.Selector, port.ConnectionType, port.FeedbackMatchObject);

				ExecuteSwitch(port.Selector);
			}

		}

		private RoutingInputPort GetInputPort(int input)
		{
			return InputPorts.ElementAt(input);
		}

		private void AddRoutingInputPort(RoutingInputPort port, string fbMatch)
		{
			port.FeedbackMatchObject = fbMatch;
			InputPorts.Add(port);
		}

		private void AddRoutingInputPort(RoutingInputPort port, byte fbMatch)
		{
			port.FeedbackMatchObject = fbMatch;
			InputPorts.Add(port);
		}

		private void InitializeRoutingPorts()
		{
			var hdmiIn1 = new RoutingInputPort(RoutingPortNames.HdmiIn1, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi1), this);
			AddRoutingInputPort(hdmiIn1, 0x10);

			var hdmiIn2 = new RoutingInputPort(RoutingPortNames.HdmiIn2, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi2), this);
			AddRoutingInputPort(hdmiIn2, 0x20);


			var hdmiIn3 = new RoutingInputPort(RoutingPortNames.HdmiIn3, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi3), this);
			AddRoutingInputPort(hdmiIn3, 0x30);

			var hdmiIn4 = new RoutingInputPort(RoutingPortNames.HdmiIn4, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi4), this);
			AddRoutingInputPort(hdmiIn4, 0x40);


			// initialize feedbacks after adding input ports
			_inputFeedback = new List<bool>();
			InputFeedback = new List<BoolFeedback>();

			for (var i = 0; i < InputPorts.Count; i++)
			{
				var input = i + 1;
				InputFeedback.Add(new BoolFeedback(() =>
				{
					Debug.Console(DebugNotice, this, "CurrentInput Number: {0}; input: {1};", CurrentInputNumber, input);
					return CurrentInputNumber == input;
				}));
			}

			CurrentInputNumberFeedback = new IntFeedback(() =>
			{
				Debug.Console(DebugVerbose, this, "CurrentInputNumberFeedback: {0}", CurrentInputNumber);
				return CurrentInputNumber;
			});
		}

		/// <summary>
		/// Lists available input routing ports
		/// </summary>
		public void ListRoutingInputPorts()
		{
			var index = 0;
			foreach (var inputPort in InputPorts)
			{
				Debug.Console(0, this, "ListRoutingInputPorts: index-'{0}' key-'{1}', connectionType-'{2}', feedbackMatchObject-'{3}'",
					index, inputPort.Key, inputPort.ConnectionType, inputPort.FeedbackMatchObject);
				index++;
			}
		}

		/// <summary>
		/// Input hdmi 1
		/// </summary>
		public void InputHdmi1()
		{
			SendBytes(CecCommands.InputHdmi1);
		}

		/// <summary>
		/// Input hdmi 2
		/// </summary>
		public void InputHdmi2()
		{
			SendBytes(CecCommands.InputHdmi2);
		}

		/// <summary>
		/// Input hdmi 3
		/// </summary>
		public void InputHdmi3()
		{
			SendBytes(CecCommands.InputHdmi3);
		}

		/// <summary>
		/// INput hdmi 4
		/// </summary>
		public void InputHdmi4()
		{
			SendBytes(CecCommands.InputHdmi4);
		}

		/// <summary>
		/// Process input feedback from device
		/// </summary>
		private void UpdateInputFb(byte b)
		{
			var newInput = InputPorts.FirstOrDefault(i => i.FeedbackMatchObject.Equals(b));

			if (newInput == null) return;

			if (newInput == _currentInputPort)
			{
				Debug.Console(DebugNotice, this, "UpdateInputFb: _currentInputPort-'{0}' == newInput-'{1}'",
					_currentInputPort.Key, newInput.Key);
				return;
			}

			Debug.Console(DebugNotice, this, "UpdateInputFb: newInput key-'{0}', connectionType-'{1}', feedbackMatchObject-'{2}'",
				newInput.Key, newInput.ConnectionType, newInput.FeedbackMatchObject);

			_currentInputPort = newInput;
			CurrentInputFeedback.FireUpdate();

			Debug.Console(DebugNotice, this, "UpdateInputFb: _currentInputPort.key-'{0}'", _currentInputPort.Key);

			switch (_currentInputPort.Key)
			{
				case RoutingPortNames.HdmiIn1:
					CurrentInputNumber = 1;
					break;
				case RoutingPortNames.HdmiIn2:
					CurrentInputNumber = 2;
					break;
				case RoutingPortNames.DviIn1:
					CurrentInputNumber = 3;
					break;
				case RoutingPortNames.VgaIn1:
					CurrentInputNumber = 4;
					break;
				case RoutingPortNames.HdmiIn5:
					CurrentInputNumber = 5;
					break;
				case RoutingPortNames.HdmiIn4:
					CurrentInputNumber = 6;
					break;
			}
		}

		/// <summary>
		/// Updates Digital Route Feedback for Simpl EISC
		/// </summary>
		private void UpdateInputBooleanFeedback()
		{
			foreach (var item in InputFeedback)
			{
				item.FireUpdate();
			}
		}

		#endregion



		#region IBasicVolumeWithFeedback Members

		private bool _isMuted;		

		/// <summary>
		/// Volume level feedback property
		/// </summary>
		public IntFeedback VolumeLevelFeedback { get; private set; }

		/// <summary>
		/// volume mte feedback property
		/// </summary>
		public BoolFeedback MuteFeedback { get; private set; }

		/// <summary>
		/// Volume up (increment)
		/// </summary>
		/// <param name="pressRelease"></param>
		public void VolumeUp(bool pressRelease)
		{
			//if (pressRelease)
			//{
			//    // _volumeDecrementer.Stop()
			//}
			//else
			//{
			//    //_volumeIncrementer.StatUp();				
			//}

			SendBytes(CecCommands.VolumeUp);
		}

		/// <summary>
		/// Volume down (decrement)
		/// </summary>
		/// <param name="pressRelease"></param>
		public void VolumeDown(bool pressRelease)
		{
			//if (pressRelease)
			//{
			//    // _volumeIncrementer.Stop(); 				
			//}
			//else
			//{
			//    //_volumeIncrementer.StartDown();
				
			//}

			SendBytes(CecCommands.VolumeDown);
		}



		// Volume feedback
		private void UpdateVolumeFb(byte volmeByte)
		{
			throw new NotImplementedException("UpdateVolumeFb not implemented");
		}

		/// <summary>
		/// </summary>
		public void MuteOn()
		{
			SendBytes(CecCommands.MuteOn);
		}

		/// <summary>
		/// </summary>
		public void MuteOff()
		{
			SendBytes(CecCommands.MuteOff);
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


		// Mute feedback
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

		#endregion




		#region DebugLevels

		private uint DebugTrace { get; set; }
		private uint DebugNotice { get; set; }
		private uint DebugVerbose { get; set; }


		public void ResetDebugLevels()
		{
			DebugTrace = 0;
			DebugNotice = 1;
			DebugVerbose = 2;
		}


		public void SetDebugLevels(uint level)
		{
			DebugTrace = level;
			DebugNotice = level;
			DebugVerbose = level;
		}

		#endregion

	}
}