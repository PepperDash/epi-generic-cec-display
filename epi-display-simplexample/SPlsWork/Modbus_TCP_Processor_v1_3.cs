using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Linq;
using Crestron;
using Crestron.Logos.SplusLibrary;
using Crestron.Logos.SplusObjects;
using Crestron.SimplSharp;

namespace UserModule_MODBUS_TCP_PROCESSOR_V1_3
{
    public class UserModuleClass_MODBUS_TCP_PROCESSOR_V1_3 : SplusObject
    {
        static CCriticalSection g_criticalSection = new CCriticalSection();
        
        
        
        Crestron.Logos.SplusObjects.BufferInput RX;
        Crestron.Logos.SplusObjects.BufferInput TO_PROCESSOR;
        Crestron.Logos.SplusObjects.StringOutput TX;
        InOutArray<Crestron.Logos.SplusObjects.StringOutput> FROM_PROCESSOR;
        ushort ARRAY_INDEX = 0;
        ushort IS_SENDING = 0;
        ushort RXOK = 0;
        CrestronString START_COM;
        CrestronString END_COM;
        private void SEND_COMMAND (  SplusExecutionContext __context__ ) 
            { 
            ushort START_MARKER = 0;
            ushort END_MARKER = 0;
            
            CrestronString TEMP;
            TEMP  = new CrestronString( Crestron.Logos.SplusObjects.CrestronStringEncoding.eEncodingASCII, 300, this );
            
            
            __context__.SourceCodeLine = 135;
            TEMP  .UpdateValue ( ""  ) ; 
            __context__.SourceCodeLine = 136;
            START_MARKER = (ushort) ( 0 ) ; 
            __context__.SourceCodeLine = 137;
            END_MARKER = (ushort) ( 0 ) ; 
            __context__.SourceCodeLine = 139;
            IS_SENDING = (ushort) ( 1 ) ; 
            __context__.SourceCodeLine = 141;
            START_MARKER = (ushort) ( Functions.Find( START_COM , TO_PROCESSOR ) ) ; 
            __context__.SourceCodeLine = 143;
            if ( Functions.TestForTrue  ( ( Functions.BoolToInt ( (Functions.TestForTrue ( Functions.BoolToInt (START_MARKER == 1) ) && Functions.TestForTrue ( Functions.BoolToInt ( Functions.Length( TO_PROCESSOR ) > 10 ) )) ))  ) ) 
                { 
                __context__.SourceCodeLine = 145;
                TEMP  .UpdateValue ( Functions.Remove ( (START_MARKER + 3), TO_PROCESSOR )  ) ; 
                __context__.SourceCodeLine = 146;
                ARRAY_INDEX = (ushort) ( Functions.GetC( TO_PROCESSOR ) ) ; 
                __context__.SourceCodeLine = 147;
                END_MARKER = (ushort) ( (Byte( TO_PROCESSOR , (int)( 6 ) ) + 6) ) ; 
                __context__.SourceCodeLine = 149;
                if ( Functions.TestForTrue  ( ( Functions.BoolToInt (Functions.Mid( TO_PROCESSOR , (int)( (END_MARKER + 1) ) , (int)( 4 ) ) == END_COM))  ) ) 
                    { 
                    __context__.SourceCodeLine = 151;
                    TX  .UpdateValue ( Functions.Remove ( END_MARKER, TO_PROCESSOR )  ) ; 
                    __context__.SourceCodeLine = 152;
                    TEMP  .UpdateValue ( Functions.Remove ( 4, TO_PROCESSOR )  ) ; 
                    __context__.SourceCodeLine = 154;
                    CreateWait ( "WAITFORRESPONSE" , 500 , WAITFORRESPONSE_Callback ) ;
                    } 
                
                else 
                    { 
                    __context__.SourceCodeLine = 164;
                    Trace( "Error in End Sign") ; 
                    __context__.SourceCodeLine = 165;
                    ARRAY_INDEX = (ushort) ( 0 ) ; 
                    __context__.SourceCodeLine = 166;
                    IS_SENDING = (ushort) ( 0 ) ; 
                    __context__.SourceCodeLine = 167;
                    Functions.ClearBuffer ( TO_PROCESSOR ) ; 
                    __context__.SourceCodeLine = 168;
                    Functions.ClearBuffer ( RX ) ; 
                    } 
                
                } 
            
            else 
                { 
                __context__.SourceCodeLine = 173;
                Trace( "Error in Start Sign") ; 
                __context__.SourceCodeLine = 174;
                ARRAY_INDEX = (ushort) ( 0 ) ; 
                __context__.SourceCodeLine = 175;
                IS_SENDING = (ushort) ( 0 ) ; 
                __context__.SourceCodeLine = 176;
                Functions.ClearBuffer ( TO_PROCESSOR ) ; 
                __context__.SourceCodeLine = 177;
                Functions.ClearBuffer ( RX ) ; 
                } 
            
            
            }
            
        public void WAITFORRESPONSE_CallbackFn( object stateInfo )
        {
        
            try
            {
                Wait __LocalWait__ = (Wait)stateInfo;
                SplusExecutionContext __context__ = SplusThreadStartCode(__LocalWait__);
                __LocalWait__.RemoveFromList();
                
            
            __context__.SourceCodeLine = 156;
            Trace( "IO Timeout {0:d}", (short)ARRAY_INDEX) ; 
            __context__.SourceCodeLine = 157;
            Functions.ClearBuffer ( TO_PROCESSOR ) ; 
            __context__.SourceCodeLine = 158;
            Functions.ClearBuffer ( RX ) ; 
            __context__.SourceCodeLine = 159;
            IS_SENDING = (ushort) ( 0 ) ; 
            
        
        
            }
            catch(Exception e) { ObjectCatchHandler(e); }
            finally { ObjectFinallyHandler(); }
            
        }
        
    object TO_PROCESSOR_OnChange_0 ( Object __EventInfo__ )
    
        { 
        Crestron.Logos.SplusObjects.SignalEventArgs __SignalEventArg__ = (Crestron.Logos.SplusObjects.SignalEventArgs)__EventInfo__;
        try
        {
            SplusExecutionContext __context__ = SplusThreadStartCode(__SignalEventArg__);
            
            __context__.SourceCodeLine = 188;
            if ( Functions.TestForTrue  ( ( Functions.BoolToInt (IS_SENDING == 0))  ) ) 
                {
                __context__.SourceCodeLine = 188;
                SEND_COMMAND (  __context__  ) ; 
                }
            
            
            
        }
        catch(Exception e) { ObjectCatchHandler(e); }
        finally { ObjectFinallyHandler( __SignalEventArg__ ); }
        return this;
        
    }
    
object RX_OnChange_1 ( Object __EventInfo__ )

    { 
    Crestron.Logos.SplusObjects.SignalEventArgs __SignalEventArg__ = (Crestron.Logos.SplusObjects.SignalEventArgs)__EventInfo__;
    try
    {
        SplusExecutionContext __context__ = SplusThreadStartCode(__SignalEventArg__);
        CrestronString HEADER;
        HEADER  = new CrestronString( Crestron.Logos.SplusObjects.CrestronStringEncoding.eEncodingASCII, 6, this );
        
        
        __context__.SourceCodeLine = 194;
        if ( Functions.TestForTrue  ( ( Functions.BoolToInt ( (Functions.TestForTrue ( Functions.BoolToInt ( ARRAY_INDEX > 0 ) ) && Functions.TestForTrue ( Functions.BoolToInt ( Functions.Length( RX ) >= 6 ) )) ))  ) ) 
            { 
            __context__.SourceCodeLine = 196;
            HEADER  .UpdateValue ( Functions.Mid ( RX ,  (int) ( 1 ) ,  (int) ( 5 ) )  ) ; 
            __context__.SourceCodeLine = 197;
            if ( Functions.TestForTrue  ( ( Functions.BoolToInt (HEADER == "\u0000\u0000\u0000\u0000\u0000"))  ) ) 
                { 
                __context__.SourceCodeLine = 199;
                if ( Functions.TestForTrue  ( ( Functions.BoolToInt ((Functions.Length( RX ) - 6) == Byte( RX , (int)( 6 ) )))  ) ) 
                    { 
                    __context__.SourceCodeLine = 201;
                    MakeString ( FROM_PROCESSOR [ ARRAY_INDEX] , "{0}{1}{2}", START_COM , RX , END_COM ) ; 
                    __context__.SourceCodeLine = 202;
                    CancelWait ( "WAITFORRESPONSE" ) ; 
                    __context__.SourceCodeLine = 203;
                    ARRAY_INDEX = (ushort) ( 0 ) ; 
                    __context__.SourceCodeLine = 204;
                    Functions.ClearBuffer ( RX ) ; 
                    __context__.SourceCodeLine = 205;
                    IS_SENDING = (ushort) ( 0 ) ; 
                    __context__.SourceCodeLine = 206;
                    if ( Functions.TestForTrue  ( ( Functions.BoolToInt (TO_PROCESSOR != ""))  ) ) 
                        {
                        __context__.SourceCodeLine = 206;
                        SEND_COMMAND (  __context__  ) ; 
                        }
                    
                    } 
                
                } 
            
            else 
                { 
                __context__.SourceCodeLine = 211;
                Functions.ClearBuffer ( RX ) ; 
                } 
            
            } 
        
        
        
    }
    catch(Exception e) { ObjectCatchHandler(e); }
    finally { ObjectFinallyHandler( __SignalEventArg__ ); }
    return this;
    
}

public override object FunctionMain (  object __obj__ ) 
    { 
    ushort D = 0;
    
    try
    {
        SplusExecutionContext __context__ = SplusFunctionMainStartCode();
        
        __context__.SourceCodeLine = 225;
        WaitForInitializationComplete ( ) ; 
        __context__.SourceCodeLine = 226;
        RXOK = (ushort) ( 1 ) ; 
        __context__.SourceCodeLine = 228;
        ushort __FN_FORSTART_VAL__1 = (ushort) ( 1 ) ;
        ushort __FN_FOREND_VAL__1 = (ushort)100; 
        int __FN_FORSTEP_VAL__1 = (int)1; 
        for ( D  = __FN_FORSTART_VAL__1; (__FN_FORSTEP_VAL__1 > 0)  ? ( (D  >= __FN_FORSTART_VAL__1) && (D  <= __FN_FOREND_VAL__1) ) : ( (D  <= __FN_FORSTART_VAL__1) && (D  >= __FN_FOREND_VAL__1) ) ; D  += (ushort)__FN_FORSTEP_VAL__1) 
            { 
            __context__.SourceCodeLine = 230;
            if ( Functions.TestForTrue  ( ( Functions.BoolToInt (IsSignalDefined( FROM_PROCESSOR[ D ] ) == 0))  ) ) 
                {
                __context__.SourceCodeLine = 230;
                break ; 
                }
            
            else 
                {
                __context__.SourceCodeLine = 231;
                FROM_PROCESSOR [ D]  .UpdateValue ( "array_index=" + Functions.Chr (  (int) ( D ) )  ) ; 
                }
            
            __context__.SourceCodeLine = 228;
            } 
        
        __context__.SourceCodeLine = 234;
        START_COM  .UpdateValue ( "\u0000\u00FF\u0000\u00FF"  ) ; 
        __context__.SourceCodeLine = 235;
        END_COM  .UpdateValue ( "\u0000\u00FE\u0000\u00FE"  ) ; 
        __context__.SourceCodeLine = 236;
        ARRAY_INDEX = (ushort) ( 0 ) ; 
        __context__.SourceCodeLine = 237;
        IS_SENDING = (ushort) ( 0 ) ; 
        
        
    }
    catch(Exception e) { ObjectCatchHandler(e); }
    finally { ObjectFinallyHandler(); }
    return __obj__;
    }
    

public override void LogosSplusInitialize()
{
    SocketInfo __socketinfo__ = new SocketInfo( 1, this );
    InitialParametersClass.ResolveHostName = __socketinfo__.ResolveHostName;
    _SplusNVRAM = new SplusNVRAM( this );
    START_COM  = new CrestronString( Crestron.Logos.SplusObjects.CrestronStringEncoding.eEncodingASCII, 5, this );
    END_COM  = new CrestronString( Crestron.Logos.SplusObjects.CrestronStringEncoding.eEncodingASCII, 5, this );
    
    TX = new Crestron.Logos.SplusObjects.StringOutput( TX__AnalogSerialOutput__, this );
    m_StringOutputList.Add( TX__AnalogSerialOutput__, TX );
    
    FROM_PROCESSOR = new InOutArray<StringOutput>( 100, this );
    for( uint i = 0; i < 100; i++ )
    {
        FROM_PROCESSOR[i+1] = new Crestron.Logos.SplusObjects.StringOutput( FROM_PROCESSOR__AnalogSerialOutput__ + i, this );
        m_StringOutputList.Add( FROM_PROCESSOR__AnalogSerialOutput__ + i, FROM_PROCESSOR[i+1] );
    }
    
    RX = new Crestron.Logos.SplusObjects.BufferInput( RX__AnalogSerialInput__, 265, this );
    m_StringInputList.Add( RX__AnalogSerialInput__, RX );
    
    TO_PROCESSOR = new Crestron.Logos.SplusObjects.BufferInput( TO_PROCESSOR__AnalogSerialInput__, 14000, this );
    m_StringInputList.Add( TO_PROCESSOR__AnalogSerialInput__, TO_PROCESSOR );
    
    WAITFORRESPONSE_Callback = new WaitFunction( WAITFORRESPONSE_CallbackFn );
    
    TO_PROCESSOR.OnSerialChange.Add( new InputChangeHandlerWrapper( TO_PROCESSOR_OnChange_0, false ) );
    RX.OnSerialChange.Add( new InputChangeHandlerWrapper( RX_OnChange_1, false ) );
    
    _SplusNVRAM.PopulateCustomAttributeList( true );
    
    NVRAM = _SplusNVRAM;
    
}

public override void LogosSimplSharpInitialize()
{
    
    
}

public UserModuleClass_MODBUS_TCP_PROCESSOR_V1_3 ( string InstanceName, string ReferenceID, Crestron.Logos.SplusObjects.CrestronStringEncoding nEncodingType ) : base( InstanceName, ReferenceID, nEncodingType ) {}


private WaitFunction WAITFORRESPONSE_Callback;


const uint RX__AnalogSerialInput__ = 0;
const uint TO_PROCESSOR__AnalogSerialInput__ = 1;
const uint TX__AnalogSerialOutput__ = 0;
const uint FROM_PROCESSOR__AnalogSerialOutput__ = 1;

[SplusStructAttribute(-1, true, false)]
public class SplusNVRAM : SplusStructureBase
{

    public SplusNVRAM( SplusObject __caller__ ) : base( __caller__ ) {}
    
    
}

SplusNVRAM _SplusNVRAM = null;

public class __CEvent__ : CEvent
{
    public __CEvent__() {}
    public void Close() { base.Close(); }
    public int Reset() { return base.Reset() ? 1 : 0; }
    public int Set() { return base.Set() ? 1 : 0; }
    public int Wait( int timeOutInMs ) { return base.Wait( timeOutInMs ) ? 1 : 0; }
}
public class __CMutex__ : CMutex
{
    public __CMutex__() {}
    public void Close() { base.Close(); }
    public void ReleaseMutex() { base.ReleaseMutex(); }
    public int WaitForMutex() { return base.WaitForMutex() ? 1 : 0; }
}
 public int IsNull( object obj ){ return (obj == null) ? 1 : 0; }
}


}
