﻿using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using AzureIoTEdgeDHTModule;
using AzureIoTEdgeAnomalyDetectModule;
using AzureIoTEdgeModuleShared;
using AzureIoTEdgeFilterModule;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;

namespace IoTEdgeAllInOne
{
    class Program
    {
        public static int SleepInterval {get; set;} = 60000;

        public static string LocalWebServerUrl {get; set;} = "http://172.17.0.1:3000/";

        public static string InteractionWebServerUrl {get; set;} = "http://172.17.0.1:3000/interact";

        public static double TempMean {get;set;} = 17.04274001359523;

        public static double HumMean {get;set;} = 58.449128802808076;

        public static double TempStdDev {get;set;} = 1.79628847515955;

        public static double HumStdDev {get;set;} = 4.81136318741087;

        public static double TempThresholdUpper {get;set;} = 35;

        public static double TempThresholdLower {get;set;} = 12;

        public static double HumThresholdUpper {get;set;} = 80;

        public static double HumThresholdLower {get;set;} = 40;

        public static string IotHubConnectionString {get;set;} = string.Empty;

        public static bool SendDataToCloud {get;set;} = true;

        public static bool IsDebug {get;set;} = false;

        public const string IOTHUBCONNECTIONSTRING = "IOTHUBCONNECTIONSTRING";

        private static string DeviceId;

        private static DeviceClient deviceClient;

        private static AnomalyDetector anomalyDetector;

        private static MachineConnector machineConnector;

        private static DHTDataLoader dataLoader;

        static void Main(string[] args)
        {
            Console.WriteLine("Starting up IoTEdgeAllInOne");

            var connectionstring = Environment.GetEnvironmentVariable(IOTHUBCONNECTIONSTRING);

            //Expect this to fail on mal formatted connectionstring
            var deviceId = connectionstring.Split(';').FirstOrDefault(e => e.StartsWith("DeviceId")).Split('=')[1];

            if(string.IsNullOrEmpty(connectionstring)){
                Console.WriteLine($"No environment variable named {IOTHUBCONNECTIONSTRING} found.");
                return;
            }

            try{
                //Expect this to fail on mal formatted connectionstring
                DeviceId = connectionstring.Split(';').FirstOrDefault(e => e.StartsWith("DeviceId")).Split('=')[1];
            }
            catch(Exception ex){
                Console.WriteLine(ex);
                return;
            }

            Init(connectionstring).Wait();  

            //start sending task
            //var t = Task.Run(() => StartMessageSending());
            var thread = new Thread(() => StartMessageSending());
            thread.Start();
            //t.Wait();         
        }

        public static async Task<bool> Init(string connectionstring){
            Console.WriteLine("Trying to initialize IoTEdgeAllInOne Container");

            //essentials from twin if available
            try {
                deviceClient = DeviceClient.CreateFromConnectionString(connectionstring, TransportType.Mqtt);
                await deviceClient.SetDesiredPropertyUpdateCallbackAsync(onDesiredPropertiesUpdate, deviceClient);
                Console.WriteLine("Module successfully conneected to IoT Hub");
            }
            catch(Exception ex){
                Console.WriteLine("Cannot connect to IoTHub... exiting.");
                Console.WriteLine(ex);
                return false;
            }

            var twin = await deviceClient.GetTwinAsync();
            await onDesiredPropertiesUpdate(twin.Properties.Desired, deviceClient);        

            //be sure object references were created
            RecreateObjectReferences();  

            var thread = new Thread(() => StartMessageReceiver());
            thread.Start();

            return true;
        }

        static public void RecreateObjectReferences(){
            dataLoader = new DHTDataLoader(LocalWebServerUrl);
            anomalyDetector = new AnomalyDetector(TempMean, TempStdDev, HumMean, HumStdDev);
            machineConnector = new MachineConnector($"{InteractionWebServerUrl}");
        }

        static public void StartMessageSending(){
            while (true)
            {
                try
                {
                    var data = dataLoader.GetDHTData();
                    if (data != null)
                    {
                        Console.WriteLine($"Read data temp: {data.Temperature}, hum: {data.Humidity}");
                        if (IsDebug)
                        {
                            Console.Write($"DEBUG: Current configuration: ");
                            Console.Write($"TempMean: {TempMean}, ");
                            Console.Write($"HumMean: {HumMean}, ");
                            Console.Write($"TempStdDev: {TempStdDev}, ");
                            Console.Write($"HumStdDev: {HumStdDev}, ");
                            Console.Write($"LocalWebserver: {LocalWebServerUrl}, ");
                            Console.Write($"SleepInterval: {SleepInterval}, ");
                            Console.Write($"TempThresholdUpper: {TempThresholdUpper}, ");
                            Console.Write($"TempThresholdLower: {TempThresholdLower}, ");
                            Console.Write($"HumThresholdUpper: {HumThresholdUpper}, ");
                            Console.Write($"HumThresholdLower: {HumThresholdLower}, ");
                            Console.Write($"SendDataToCloud: {SendDataToCloud}, ");
                            Console.WriteLine("");
                        }

                        var isAnomaly = anomalyDetector.IsAnomaly(data.Temperature, data.Humidity);
                        var isCritical = data.Temperature < TempThresholdLower ||
                                            data.Temperature > TempThresholdUpper ||
                                            data.Humidity < HumThresholdLower ||
                                            data.Humidity > HumThresholdUpper;

                        if (IsDebug)
                        {
                            Console.Write($"IsAnomaly: {isAnomaly}, IsCritical: {isCritical}");
                            Console.WriteLine("");
                        }

                        DeviceToCloudMessage d2cMessage = null;
                        if (isAnomaly || isCritical)
                        {

                            d2cMessage = new DeviceToCloudMessage()
                            {
                                Temperature = data.Temperature,
                                Humidity = data.Humidity,
                                MessageLevel = (int)(isCritical ? DeviceToCloudMessageLevel.Critical : DeviceToCloudMessageLevel.Warning),
                                DeviceId = DeviceId
                            };

                            var level = isCritical ? InteractionCommandLevel.Critical : InteractionCommandLevel.Warning;
                            machineConnector.InteractWithMachine(new MachineInteractionCommand()
                            {
                                CommandLevel = level,
                                Temperature = data.Temperature,
                                Humidity = data.Humidity
                            });
                        }
                        else if (SendDataToCloud)
                        {
                            d2cMessage = new DeviceToCloudMessage()
                            {
                                Temperature = data.Temperature,
                                Humidity = data.Humidity,
                                MessageLevel = (int)DeviceToCloudMessageLevel.Info,
                                DeviceId = DeviceId
                            };
                        }

                        if (d2cMessage != null)
                        {
                            SendCloudMessage(d2cMessage);
                        }
                    }
                    else
                    {
                        Console.WriteLine("Error: Cannot read data from DHT sensor");
                    }

                    Thread.Sleep(SleepInterval);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception occured during message sending: {ex.Message}");
                    Console.WriteLine(ex);
                }
            }

        }
       
        static async public void StartMessageReceiver(){
            while (true)
            {
                try
                {
                    Message receivedMessage = await deviceClient.ReceiveAsync();
                    if (receivedMessage == null) continue;

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    var messageString = Encoding.ASCII.GetString(receivedMessage.GetBytes());
                    Console.WriteLine("Received message: {0}", messageString);
                    Console.ResetColor();

                    if (machineConnector.SendText(new MachineSendTextCommand()
                    {
                        Text = messageString,
                        CommandLevel = InteractionCommandLevel.Info
                    }))
                    {
                        Console.WriteLine($"Successfully send text to local machine");
                    }

                    await deviceClient.CompleteAsync(receivedMessage);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception occured during message receiving");
                    Console.WriteLine(ex.Message);
                }
            }
        }

        static public Task onDesiredPropertiesUpdate(TwinCollection desiredProperties, object userContext){
            if (desiredProperties.Count == 0)
            {
                return Task.CompletedTask;
            }

            try
            {
                Console.WriteLine("Desired property change. Received Content:");
                Console.WriteLine(JsonConvert.SerializeObject(desiredProperties));

                var deviceClient = userContext as DeviceClient;

                if (deviceClient == null)
                {
                    throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
                }

                if (CheckProperty(desiredProperties, "TemperatureThresholdUpper"))
                {
                    TempThresholdUpper = desiredProperties["TemperatureThresholdUpper"];        
                    Console.WriteLine($"Updated TempThresholdUpper to {TempThresholdUpper}");          
                }

                if (CheckProperty(desiredProperties, "TemperatureThresholdLower"))
                {
                    TempThresholdLower = desiredProperties["TemperatureThresholdLower"];
                    Console.WriteLine($"Updated TempThresholdLower to {TempThresholdLower}");                     
                }

                if (CheckProperty(desiredProperties, "HumidityThresholdUpper"))
                {
                    HumThresholdUpper = desiredProperties["HumidityThresholdUpper"]; 
                    Console.WriteLine($"Updated HumThresholdUpper to {HumThresholdUpper}");                      
                }

                if (CheckProperty(desiredProperties, "HumidityThresholdLower"))
                {
                    HumThresholdLower = desiredProperties["HumidityThresholdLower"];
                    Console.WriteLine($"Updated HumThresholdLower to {HumThresholdLower}");                    
                }
                
                if (CheckProperty(desiredProperties, "WebServerUrl"))
                {
                    LocalWebServerUrl = desiredProperties["WebServerUrl"];
                    Console.WriteLine($"Updated LocalWebServerUrl to {LocalWebServerUrl}");                  
                }

                if (CheckProperty(desiredProperties, "InteractionWebServerUrl"))
                {
                    InteractionWebServerUrl = desiredProperties["InteractionWebServerUrl"];
                    Console.WriteLine($"Updated InteractionWebServerUrl to {InteractionWebServerUrl}");                  
                }

                if (CheckProperty(desiredProperties, "TemperatureMeanValue"))
                {
                    TempMean = desiredProperties["TemperatureMeanValue"];
                    Console.WriteLine($"Updated TempMean to {TempMean}");                    
                }

                if (CheckProperty(desiredProperties, "TemperatureStdDeviation"))
                {
                    TempStdDev = desiredProperties["TemperatureStdDeviation"];   
                    Console.WriteLine($"Updated TempStdDev to {TempStdDev}");                  
                }

                if (CheckProperty(desiredProperties, "HumidityMeanValue"))
                {
                    HumMean = desiredProperties["HumidityMeanValue"];
                    Console.WriteLine($"Updated HumMean to {HumMean}");                    
                }

                if (CheckProperty(desiredProperties, "HumidityStdDeviation"))
                {
                    HumStdDev = desiredProperties["HumidityStdDeviation"]; 
                    Console.WriteLine($"Updated HumStdDev to {HumStdDev}");                    
                }

                if (CheckProperty(desiredProperties, "Interval"))
                {
                    SleepInterval = desiredProperties["Interval"]; 
                    Console.WriteLine($"Updated SleepInterval to {SleepInterval}");                   
                }

                if (CheckProperty(desiredProperties, "SendDataToCloud"))
                {
                    SendDataToCloud = desiredProperties["SendDataToCloud"]; 
                    Console.WriteLine($"Updated SendDataToCloud to {SendDataToCloud}");                                                   
                }

                if (CheckProperty(desiredProperties, "DebugMode"))
                {
                    IsDebug = desiredProperties["DebugMode"]; 
                    Console.WriteLine($"Updated DebugMode to {IsDebug}");                        
                }

                //if anything changed, recreate object references too
                RecreateObjectReferences();
            }
            catch (AggregateException ex)
            {
                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine();
                    Console.WriteLine("Error when receiving desired property: {0}", exception);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Error when receiving desired property: {0}", ex.Message);
            }

            return Task.CompletedTask;
        }

        private static bool CheckProperty(TwinCollection desiredProperties, string propertyName){
            return desiredProperties.Contains(propertyName) && desiredProperties[propertyName] != null;
        }

        private static async void SendCloudMessage(DeviceToCloudMessage d2cMessage){
            try{
                var messageString = JsonConvert.SerializeObject(d2cMessage);
                var message = new Message(Encoding.ASCII.GetBytes(messageString));
                await deviceClient.SendEventAsync(message);
            }
            catch(Exception ex){
                Console.WriteLine($"Exception occured on sending data to IoT Hub: {ex.Message}");
                Console.WriteLine(ex);
            }
        }
    }
}
