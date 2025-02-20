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
    public class CecSoundBarController : EssentialsDevice, ICommunicationMonitor, IHasPowerControlWithFeedback
    {
        public const int InputPowerOn = 101;
        public const int InputPowerOff = 102;
        public static List<string> InputKeys = new List<string>();
        private readonly CecSoundBarPropertiesConfig _config;

        private readonly long _pollIntervalMs;
 


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

            Id = _config.Id == null ? (byte) 0x01 : Convert.ToByte(_config.Id, 16);


            
            _pollIntervalMs = _config.pollIntervalMs;
            


            PowerIsOnFeedback = new BoolFeedback(() => _powerIsOn);

            Init();
        }

        public IBasicCommunication Communication { get; private set; }
        public byte Id { get; private set; }
        public IntFeedback StatusFeedback { get; set; }

        


       


        protected override Func<string> CurrentInputFeedbackFunc
        {
            get { return () => _currentInputPort.Key; }
        }

        /// <summary>
        /// 
        /// </summary>
        

        #region Command Constants

        /// <summary>
        /// Power control on 
        /// </summary>
        public const string PowerControlOn = "\x4F\x82\x11\x00"; //Note, this is for "Active Source HDMI", we need "Active Source Optical" or discrete Power On may also be "One Touch Play"

        /// <summary>
        /// Power control off
        /// </summary>
        public const string PowerControlOff = "\x1F\x36";


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

            StatusFeedback = new IntFeedback(() => (int) CommunicationMonitor.Status);

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

        

        
        

        /// <summary>
        /// </summary>
        public void StatusGet()
        {
			   Communication.SendText("\x40\x8F");
            
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

            if (PowerIsOnFeedback.BoolValue)
            {
                return;
            }
            
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
          
				Communication.SendText(PowerControlOff);
                
				CurrentInputNumber = 0;
                
                InputNumberFeedback.FireUpdate();
                PowerIsOnFeedback.FireUpdate();
                
            
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
                    

                   
                    var action = selector as Action;
                    if (action != null)
                    {
                        action();
                    }
                };
               
                PowerOn();
            }
        }




        #region IHasPowerControlWithFeedback Members

        BoolFeedback PowerIsOnFeedback
        {
            get;
            private set;
        }

            /*
             * = new BoolFeedback(() => _powerIsOn);*/
      

        #endregion

       
    }
}