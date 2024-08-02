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

public class DeviceInfoLogger : MonoBehaviour
{
    public Text logText;
    private List<string> logs = new List<string>();
    private float logInterval = 5f; // Interval for logging network data and Wi-Fi signal strength
    private static readonly ILog log = LogManager.GetLogger(typeof(DeviceInfoLogger));

    private IConnection rabbitMQConnection;
    private IModel rabbitMQChannel;

    void Start()
    {
        // Configure log4net
        var logRepository = LogManager.GetRepository(System.Reflection.Assembly.GetExecutingAssembly());
        XmlConfigurator.Configure(logRepository, new FileInfo("Assets/log4net.config"));

        // Configure RabbitMQ
        var factory = new ConnectionFactory() { HostName = "localhost" }; // Change to your RabbitMQ server hostname
        rabbitMQConnection = factory.CreateConnection();
        rabbitMQChannel = rabbitMQConnection.CreateModel();
        rabbitMQChannel.QueueDeclare(queue: "device_logs", durable: false, exclusive: false, autoDelete: false, arguments: null);

        StartCoroutine(LogDeviceInfo());
    }

    void OnDestroy()
    {
        rabbitMQChannel.Close();
        rabbitMQConnection.Close();
    }

    void Update()
    {
        LogAccelerometerData();
        UpdateLogText();
    }

    IEnumerator LogDeviceInfo()
    {
        while (true)
        {
            LogUserInfo();
            yield return StartCoroutine(LogGeolocationData());
            LogAvailableDevices();
            LogWifiSignalStrength();
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
        Vector3 acceleration = Input.acceleration;
        string accelerometerData = $"Accelerometer: x={acceleration.x}, y={acceleration.y}, z={acceleration.z}";
        AddLog(accelerometerData);
        log.Info(accelerometerData);
        SendMessageToRabbitMQ(accelerometerData);
    }

    void LogAvailableDevices()
    {
        foreach (NetworkInterface netInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (netInterface.OperationalStatus == OperationalStatus.Up)
            {
                foreach (UnicastIPAddressInformation ip in netInterface.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        Ping ping = new Ping();
                        PingReply reply = ping.Send(ip.Address);
                        if (reply.Status == IPStatus.Success)
                        {
                            string deviceInfo = $"Device: {ip.Address}, Ping: {reply.RoundtripTime}ms";
                            AddLog(deviceInfo);
                            log.Info(deviceInfo);
                            SendMessageToRabbitMQ(deviceInfo);
                        }
                    }
                }
            }
        }
    }

    void LogWifiSignalStrength()
    {
#if UNITY_ANDROID
        AndroidJavaClass wifiManagerClass = new AndroidJavaClass("android.net.wifi.WifiManager");
        AndroidJavaObject wifiManager = wifiManagerClass.CallStatic<AndroidJavaObject>("getSystemService", "wifi");
        AndroidJavaObject wifiInfo = wifiManager.Call<AndroidJavaObject>("getConnectionInfo");
        int signalStrength = wifiInfo.Call<int>("getRssi");
        string wifiSignal = $"Wi-Fi Signal Strength: {signalStrength} dBm";
        AddLog(wifiSignal);
        log.Info(wifiSignal);
        SendMessageToRabbitMQ(wifiSignal);
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

    // This method would call an iOS plugin to get Wi-Fi signal strength
    string GetIOSWiFiSignalStrength()
    {
        // Implement the native iOS plugin logic here
        // For the sake of this example, we'll return a dummy value
        return "Wi-Fi Signal Strength: -50 dBm";
    }

    void AddLog(string message)
    {
        logs.Add($"{System.DateTime.Now}: {message}");
        if (logs.Count > 20)
        {
            logs.RemoveAt(0);
        }
    }

    void UpdateLogText()
    {
        logText.text = string.Join("\n", logs.ToArray());
    }

    void SendMessageToRabbitMQ(string message)
    {
        var body = Encoding.UTF8.GetBytes(message);
        rabbitMQChannel.BasicPublish(exchange: "", routingKey: "device_logs", basicProperties: null, body: body);
    }
}
