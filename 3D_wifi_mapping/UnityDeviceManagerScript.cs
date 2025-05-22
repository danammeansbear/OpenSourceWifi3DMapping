using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net;
using System.Linq;
using System.Net.Sockets;
using log4net;
using log4net.Config;
using System.IO;
using RabbitMQ.Client;
using System.Text;
using System;

public class DeviceInfoLogger : MonoBehaviour
{
    public Text logText;
    [SerializeField] private float logInterval = 5f; // Interval for logging network data and Wi-Fi signal strength
    [SerializeField] private float accelerometerLogInterval = 1f; // Interval for logging accelerometer data
    [SerializeField] private string rabbitMQHostName = "localhost";
    [SerializeField] private string rabbitMQQueueName = "device_logs";

    private List<string> logs = new List<string>();
    private static readonly ILog log = LogManager.GetLogger(typeof(DeviceInfoLogger));

    private IConnection rabbitMQConnection;
    private IModel rabbitMQChannel;
    private bool rabbitMQConnected = false;
    private Coroutine accelerometerCoroutine;
    private Coroutine deviceInfoCoroutine;

    void Start()
    {
        // Configure log4net
        try
        {
            var logRepository = LogManager.GetRepository(System.Reflection.Assembly.GetExecutingAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("Assets/log4net.config"));
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to configure log4net: {ex.Message}");
        }

        // Configure RabbitMQ
        ConnectToRabbitMQ();

        // Start logging coroutines
        deviceInfoCoroutine = StartCoroutine(LogDeviceInfo());
        accelerometerCoroutine = StartCoroutine(LogAccelerometerDataPeriodically());
    }

    void ConnectToRabbitMQ()
    {
        try
        {
            var factory = new ConnectionFactory() { HostName = rabbitMQHostName };
            rabbitMQConnection = factory.CreateConnection();
            rabbitMQChannel = rabbitMQConnection.CreateModel();
            rabbitMQChannel.QueueDeclare(queue: rabbitMQQueueName, durable: false, exclusive: false, autoDelete: false, arguments: null);
            rabbitMQConnected = true;
            AddLog("Successfully connected to RabbitMQ");
        }
        catch (Exception ex)
        {
            rabbitMQConnected = false;
            string errorMessage = $"Failed to connect to RabbitMQ: {ex.Message}";
            AddLog(errorMessage);
            log.Error(errorMessage);
            Debug.LogError(errorMessage);
        }
    }

    void OnDestroy()
    {
        if (deviceInfoCoroutine != null)
            StopCoroutine(deviceInfoCoroutine);
        
        if (accelerometerCoroutine != null)
            StopCoroutine(accelerometerCoroutine);

        if (rabbitMQConnected)
        {
            try
            {
                rabbitMQChannel?.Close();
                rabbitMQConnection?.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error closing RabbitMQ connection: {ex.Message}");
            }
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            // App is paused
            if (deviceInfoCoroutine != null)
                StopCoroutine(deviceInfoCoroutine);
            
            if (accelerometerCoroutine != null)
                StopCoroutine(accelerometerCoroutine);
        }
        else
        {
            // App is resumed
            deviceInfoCoroutine = StartCoroutine(LogDeviceInfo());
            accelerometerCoroutine = StartCoroutine(LogAccelerometerDataPeriodically());
        }
    }

    void Update()
    {
        UpdateLogText();
    }

    IEnumerator LogAccelerometerDataPeriodically()
    {
        while (true)
        {
            LogAccelerometerData();
            yield return new WaitForSeconds(accelerometerLogInterval);
        }
    }

    IEnumerator LogDeviceInfo()
    {
        while (true)
        {
            try
            {
                LogUserInfo();
                yield return StartCoroutine(LogGeolocationData());
                StartCoroutine(LogAvailableDevicesAsync());
                LogWifiSignalStrength();
            }
            catch (Exception ex)
            {
                string errorMessage = $"Error in LogDeviceInfo: {ex.Message}";
                AddLog(errorMessage);
                log.Error(errorMessage);
                Debug.LogError(errorMessage);
            }
            yield return new WaitForSeconds(logInterval);
        }
    }

    void LogUserInfo()
    {
        string userInfo = $"User: {SystemInfo.deviceName}, OS: {SystemInfo.operatingSystem}, Device Model: {SystemInfo.deviceModel}";
        AddLog(userInfo);
        log.Info(userInfo);
        SendMessageToRabbitMQ(userInfo);
    }

    IEnumerator LogGeolocationData()
    {
        if (!Input.location.isEnabledByUser)
        {
            string message = "Geolocation not enabled.";
            AddLog(message);
            log.Warn(message);
            SendMessageToRabbitMQ(message);
            yield break;
        }

        Input.location.Start();

        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (maxWait <= 0)
        {
            string message = "Geolocation initialization timed out.";
            AddLog(message);
            log.Error(message);
            SendMessageToRabbitMQ(message);
            yield break;
        }

        if (Input.location.status == LocationServiceStatus.Failed)
        {
            string message = "Unable to determine device location.";
            AddLog(message);
            log.Error(message);
            SendMessageToRabbitMQ(message);
        }
        else
        {
            string geoData = $"Location: Latitude {Input.location.lastData.latitude}, Longitude {Input.location.lastData.longitude}, Altitude {Input.location.lastData.altitude}, Horizontal Accuracy {Input.location.lastData.horizontalAccuracy}, Timestamp {Input.location.lastData.timestamp}";
            AddLog(geoData);
            log.Info(geoData);
            SendMessageToRabbitMQ(geoData);
        }

        Input.location.Stop();
    }

    void LogAccelerometerData()
    {
        try
        {
            Vector3 acceleration = Input.acceleration;
            string accelerometerData = $"Accelerometer: x={acceleration.x:F2}, y={acceleration.y:F2}, z={acceleration.z:F2}";
            AddLog(accelerometerData);
            log.Info(accelerometerData);
            SendMessageToRabbitMQ(accelerometerData);
        }
        catch (Exception ex)
        {
            string errorMessage = $"Error logging accelerometer data: {ex.Message}";
            AddLog(errorMessage);
            log.Error(errorMessage);
            Debug.LogError(errorMessage);
        }
    }

    IEnumerator LogAvailableDevicesAsync()
    {
        try
        {
            foreach (NetworkInterface netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (netInterface.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation ip in netInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            yield return StartCoroutine(PingAddressAsync(ip.Address));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            string errorMessage = $"Error logging network devices: {ex.Message}";
            AddLog(errorMessage);
            log.Error(errorMessage);
            Debug.LogError(errorMessage);
        }
    }

    IEnumerator PingAddressAsync(IPAddress address)
    {
        try
        {
            Ping ping = new Ping(address.ToString());
            
            // Wait for ping to complete (with timeout)
            float startTime = Time.time;
            while (!ping.isDone && Time.time - startTime < 2f)
            {
                yield return null;
            }
            
            if (ping.isDone && ping.time >= 0)
            {
                string deviceInfo = $"Device: {address}, Ping: {ping.time}ms";
                AddLog(deviceInfo);
                log.Info(deviceInfo);
                SendMessageToRabbitMQ(deviceInfo);
            }
        }
        catch (Exception ex)
        {
            string errorMessage = $"Error pinging address {address}: {ex.Message}";
            AddLog(errorMessage);
            log.Error(errorMessage);
            Debug.LogError(errorMessage);
        }
    }

    void LogWifiSignalStrength()
    {
        try
        {
#if UNITY_ANDROID
            using (AndroidJavaObject activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject context = activity.Call<AndroidJavaObject>("getApplicationContext"))
            using (AndroidJavaObject wifiManager = context.Call<AndroidJavaObject>("getSystemService", "wifi"))
            {
                AndroidJavaObject wifiInfo = wifiManager.Call<AndroidJavaObject>("getConnectionInfo");
                int signalStrength = wifiInfo.Call<int>("getRssi");
                string wifiSignal = $"Wi-Fi Signal Strength: {signalStrength} dBm";
                AddLog(wifiSignal);
                log.Info(wifiSignal);
                SendMessageToRabbitMQ(wifiSignal);
            }
#elif UNITY_IOS
            // Assuming you have a native iOS plugin to get Wi-Fi signal strength
            string wifiSignal = GetIOSWiFiSignalStrength();
            AddLog(wifiSignal);
            log.Info(wifiSignal);
            SendMessageToRabbitMQ(wifiSignal);
#else
            string message = "Wi-Fi Signal Strength not available on this platform.";
            AddLog(message);
            log.Warn(message);
            SendMessageToRabbitMQ(message);
#endif
        }
        catch (Exception ex)
        {
            string errorMessage = $"Error getting WiFi signal strength: {ex.Message}";
            AddLog(errorMessage);
            log.Error(errorMessage);
            Debug.LogError(errorMessage);
        }
    }

    // This method would call an iOS plugin to get Wi-Fi signal strength
    string GetIOSWiFiSignalStrength()
    {
        // Implement the native iOS plugin logic here
        // For the sake of this example, we'll return a dummy value
        return "Wi-Fi Signal Strength: -50 dBm";
    }

    void AddLog(string message)
    {
        logs.Add($"{DateTime.Now.ToString("HH:mm:ss")}: {message}");
        if (logs.Count > 20)
        {
            logs.RemoveAt(0);
        }
    }

    void UpdateLogText()
    {
        if (logText != null)
        {
            logText.text = string.Join("\n", logs.ToArray());
        }
    }

    void SendMessageToRabbitMQ(string message)
    {
        if (!rabbitMQConnected)
        {
            // Try to reconnect
            ConnectToRabbitMQ();
            if (!rabbitMQConnected)
                return;
        }

        try
        {
            var body = Encoding.UTF8.GetBytes(message);
            rabbitMQChannel.BasicPublish(exchange: "", routingKey: rabbitMQQueueName, basicProperties: null, body: body);
        }
        catch (Exception ex)
        {
            rabbitMQConnected = false;
            string errorMessage = $"Error sending message to RabbitMQ: {ex.Message}";
            AddLog(errorMessage);
            log.Error(errorMessage);
            Debug.LogError(errorMessage);
        }
    }
}
