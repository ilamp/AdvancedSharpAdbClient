// <copyright file="AdbClient.cs" company="The Android Open Source Project, Ryan Conrad, Quamotion, yungd1plomat, wherewhere">
// Copyright (c) The Android Open Source Project, Ryan Conrad, Quamotion, yungd1plomat, wherewhere. All rights reserved.
// </copyright>

using AdvancedSharpAdbClient.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;

#if NET
using System.Runtime.Versioning;
#endif

namespace AdvancedSharpAdbClient
{
    /// <summary>
    /// <para>
    /// Implements the <see cref="IAdbClient"/> interface, and allows you to interact with the
    /// adb server and devices that are connected to that adb server.
    /// </para>
    /// <para>
    /// For example, to fetch a list of all devices that are currently connected to this PC, you can
    /// call the <see cref="GetDevices"/> method.
    /// </para>
    /// <para>
    /// To run a command on a device, you can use the <see cref="ExecuteRemoteCommandAsync(string, DeviceData, IShellOutputReceiver, CancellationToken)"/>
    /// method.
    /// </para>
    /// </summary>
    /// <remarks><para><seealso href="https://github.com/android/platform_system_core/blob/master/adb/SERVICES.TXT">SERVICES.TXT</seealso></para>
    /// <para><seealso href="https://github.com/android/platform_system_core/blob/master/adb/adb_client.c">adb_client.c</seealso></para>
    /// <para><seealso href="https://github.com/android/platform_system_core/blob/master/adb/adb.c">adb.c</seealso></para></remarks>
    public partial class AdbClient : IAdbClient
    {
        /// <summary>
        /// The port at which the Android Debug Bridge server listens by default.
        /// </summary>
        public const int AdbServerPort = 5037;

        /// <summary>
        /// The default port to use when connecting to a device over TCP/IP.
        /// </summary>
        public const int DefaultPort = 5555;

        /// <summary>
        /// Gets a new instance of the <see cref="AdbClient"/> class.
        /// </summary>
        [Obsolete("This function has been removed since SharpAdbClient. Here is a placeholder which function is gets a new instance instead of gets or sets the default instance.")]
        public static IAdbClient Instance => new AdbClient();

        private readonly Func<EndPoint, IAdbSocket> adbSocketFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="AdbClient"/> class.
        /// </summary>
        public AdbClient() : this(new IPEndPoint(IPAddress.Loopback, AdbServerPort), Factories.AdbSocketFactory)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AdbClient"/> class.
        /// </summary>
        /// <param name="endPoint">The <see cref="System.Net.EndPoint"/> at which the adb server is listening.</param>
        /// <param name="adbSocketFactory">The <see cref="Func{EndPoint, IAdbSocket}"/> to create <see cref="IAdbSocket"/>.</param>
        public AdbClient(EndPoint endPoint, Func<EndPoint, IAdbSocket> adbSocketFactory)
        {
            if (endPoint == null)
            {
                throw new ArgumentNullException(nameof(endPoint));
            }

            if (endPoint is not (IPEndPoint or DnsEndPoint))
            {
                throw new NotSupportedException("Only TCP endpoints are supported");
            }

            EndPoint = endPoint;
            this.adbSocketFactory = adbSocketFactory ?? throw new ArgumentNullException(nameof(adbSocketFactory));
        }

        /// <summary>
        /// Get or set default encoding
        /// </summary>
        public static Encoding Encoding { get; set; } = Encoding.UTF8;

        /// <summary>
        /// The Default <see cref="System.Net.EndPoint"/> at which the adb server is listening.
        /// </summary>
        public static EndPoint DefaultEndPoint => new IPEndPoint(IPAddress.Loopback, DefaultPort);

        /// <summary>
        /// Gets the <see cref="System.Net.EndPoint"/> at which the adb server is listening.
        /// </summary>
        public EndPoint EndPoint { get; private set; }

        /// <summary>
        /// Create an ASCII string preceded by four hex digits. The opening "####"
        /// is the length of the rest of the string, encoded as ASCII hex(case
        /// doesn't matter).
        /// </summary>
        /// <param name="req">The request to form.</param>
        /// <returns>An array containing <c>####req</c>.</returns>
        public static byte[] FormAdbRequest(string req)
        {
            int payloadLength = Encoding.GetByteCount(req);
            string resultStr = string.Format("{0}{1}", payloadLength.ToString("X4"), req);
            byte[] result = Encoding.GetBytes(resultStr);
            return result;
        }

        /// <summary>
        /// Creates the adb forward request.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <param name="port">The port.</param>
        /// <returns>This returns an array containing <c>"####tcp:{port}:{addStr}"</c>.</returns>
        public static byte[] CreateAdbForwardRequest(string address, int port)
        {
            string request = address == null ? $"tcp:{port}" : $"tcp:{port}:{address}";
            return FormAdbRequest(request);
        }

        /// <inheritdoc/>
        public int GetAdbVersion()
        {
            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SendAdbRequest("host:version");
            AdbResponse response = socket.ReadAdbResponse();
            string version = socket.ReadString();

            return int.Parse(version, NumberStyles.HexNumber);
        }

        /// <inheritdoc/>
        public void KillAdb()
        {
            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SendAdbRequest("host:kill");

            // The host will immediately close the connection after the kill
            // command has been sent; no need to read the response.
        }

        /// <inheritdoc/>
        public List<DeviceData> GetDevices()
        {
            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SendAdbRequest("host:devices-l");
            socket.ReadAdbResponse();
            string reply = socket.ReadString();

            string[] data = reply.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            return data.Select(DeviceData.CreateFromAdbData).ToList();
        }

        /// <inheritdoc/>
        public int CreateForward(DeviceData device, string local, string remote, bool allowRebind)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            string rebind = allowRebind ? string.Empty : "norebind:";

            socket.SendAdbRequest($"host-serial:{device.Serial}:forward:{rebind}{local};{remote}");
            _ = socket.ReadAdbResponse();
            _ = socket.ReadAdbResponse();
            string portString = socket.ReadString();

            return portString != null && int.TryParse(portString, out int port) ? port : 0;
        }

        /// <inheritdoc/>
        public int CreateForward(DeviceData device, ForwardSpec local, ForwardSpec remote, bool allowRebind) =>
            CreateForward(device, local?.ToString(), remote?.ToString(), allowRebind);

        /// <inheritdoc/>
        public int CreateReverseForward(DeviceData device, string remote, string local, bool allowRebind)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);

            string rebind = allowRebind ? string.Empty : "norebind:";

            socket.SendAdbRequest($"reverse:forward:{rebind}{remote};{local}");
            _ = socket.ReadAdbResponse();
            _ = socket.ReadAdbResponse();
            string portString = socket.ReadString();

            return portString != null && int.TryParse(portString, out int port) ? port : 0;
        }

        /// <inheritdoc/>
        public void RemoveReverseForward(DeviceData device, string remote)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);

            socket.SendAdbRequest($"reverse:killforward:{remote}");
            AdbResponse response = socket.ReadAdbResponse();
        }

        /// <inheritdoc/>
        public void RemoveAllReverseForwards(DeviceData device)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);

            socket.SendAdbRequest($"reverse:killforward-all");
            AdbResponse response = socket.ReadAdbResponse();
        }

        /// <inheritdoc/>
        public void RemoveForward(DeviceData device, int localPort)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SendAdbRequest($"host-serial:{device.Serial}:killforward:tcp:{localPort}");
            AdbResponse response = socket.ReadAdbResponse();
        }

        /// <inheritdoc/>
        public void RemoveAllForwards(DeviceData device)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SendAdbRequest($"host-serial:{device.Serial}:killforward-all");
            AdbResponse response = socket.ReadAdbResponse();
        }

        /// <inheritdoc/>
        public IEnumerable<ForwardData> ListForward(DeviceData device)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SendAdbRequest($"host-serial:{device.Serial}:list-forward");
            AdbResponse response = socket.ReadAdbResponse();

            string data = socket.ReadString();

            string[] parts = data.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            return parts.Select(ForwardData.FromString);
        }

        /// <inheritdoc/>
        public IEnumerable<ForwardData> ListReverseForward(DeviceData device)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);

            socket.SendAdbRequest($"reverse:list-forward");
            AdbResponse response = socket.ReadAdbResponse();

            string data = socket.ReadString();

            string[] parts = data.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            return parts.Select(ForwardData.FromString);
        }

        /// <inheritdoc/>
        public void ExecuteRemoteCommand(string command, DeviceData device, IShellOutputReceiver receiver) =>
            ExecuteRemoteCommand(command, device, receiver, Encoding);

        /// <inheritdoc/>
        public void ExecuteRemoteCommand(string command, DeviceData device, IShellOutputReceiver receiver, Encoding encoding)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);

            socket.SetDevice(device);
            socket.SendAdbRequest($"shell:{command}");
            AdbResponse response = socket.ReadAdbResponse();

            try
            {
                using StreamReader reader = new(socket.GetShellStream(), encoding);
                // Previously, we would loop while reader.Peek() >= 0. Turns out that this would
                // break too soon in certain cases (about every 10 loops, so it appears to be a timing
                // issue). Checking for reader.ReadLine() to return null appears to be much more robust
                // -- one of the integration test fetches output 1000 times and found no truncations.
                while (true)
                {
                    string line = reader.ReadLine();

                    if (line == null) { break; }

                    receiver?.AddOutput(line);
                }
            }
            finally
            {
                receiver?.Flush();
            }
        }

        /// <inheritdoc/>
        public Framebuffer CreateRefreshableFramebuffer(DeviceData device)
        {
            EnsureDevice(device);

            return new Framebuffer(device, this);
        }

        /// <inheritdoc/>
        public void Reboot(string into, DeviceData device)
        {
            EnsureDevice(device);

            string request = $"reboot:{into}";

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);
            socket.SendAdbRequest(request);
            AdbResponse response = socket.ReadAdbResponse();
        }

        /// <inheritdoc/>
        public string Pair(DnsEndPoint endpoint, string code)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SendAdbRequest($"host:pair:{code}:{endpoint.Host}:{endpoint.Port}");
            AdbResponse response = socket.ReadAdbResponse();
            string results = socket.ReadString();
            return results;
        }

        /// <inheritdoc/>
        public string Connect(DnsEndPoint endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SendAdbRequest($"host:connect:{endpoint.Host}:{endpoint.Port}");
            AdbResponse response = socket.ReadAdbResponse();
            string results = socket.ReadString();
            return results;
        }

        /// <inheritdoc/>
        public string Disconnect(DnsEndPoint endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SendAdbRequest($"host:disconnect:{endpoint.Host}:{endpoint.Port}");
            AdbResponse response = socket.ReadAdbResponse();
            string results = socket.ReadString();
            return results;
        }

        /// <inheritdoc/>
        public void Root(DeviceData device) => Root("root:", device);

        /// <inheritdoc/>
        public void Unroot(DeviceData device) => Root("unroot:", device);

        /// <summary>
        /// Restarts the ADB daemon running on the device with or without root privileges.
        /// </summary>
        /// <param name="request">The command of root or unroot.</param>
        /// <param name="device">The device on which to restart ADB with root privileges.</param>
        protected void Root(string request, DeviceData device)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);
            socket.SendAdbRequest(request);
            AdbResponse response = socket.ReadAdbResponse();

            // ADB will send some additional data
            byte[] buffer = new byte[1024];
            int read = socket.Read(buffer);

            string responseMessage = Encoding.UTF8.GetString(buffer, 0, read);

            // see https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/master/daemon/restart_service.cpp
            // for possible return strings
            if (responseMessage.IndexOf("restarting", StringComparison.OrdinalIgnoreCase) == -1)
            {
                throw new AdbException(responseMessage);
            }
            else
            {
                // Give adbd some time to kill itself and come back up.
                // We can't use wait-for-device because devices (e.g. adb over network) might not come back.
                Utilities.Delay(3000).GetAwaiter().GetResult();
            }
        }

        /// <inheritdoc/>
        public void Install(DeviceData device, Stream apk, params string[] arguments)
        {
            EnsureDevice(device);

            if (apk == null)
            {
                throw new ArgumentNullException(nameof(apk));
            }

            if (!apk.CanRead || !apk.CanSeek)
            {
                throw new ArgumentOutOfRangeException(nameof(apk), "The apk stream must be a readable and seekable stream");
            }

            StringBuilder requestBuilder = new();
            _ = requestBuilder.Append("exec:cmd package 'install' ");

            if (arguments != null)
            {
                foreach (string argument in arguments)
                {
                    _ = requestBuilder.Append(' ');
                    _ = requestBuilder.Append(argument);
                }
            }

            // add size parameter [required for streaming installs]
            // do last to override any user specified value
            _ = requestBuilder.Append($" -S {apk.Length}");

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);

            socket.SendAdbRequest(requestBuilder.ToString());
            AdbResponse response = socket.ReadAdbResponse();

            byte[] buffer = new byte[32 * 1024];
            int read = 0;

            while ((read = apk.Read(buffer, 0, buffer.Length)) > 0)
            {
                socket.Send(buffer, read);
            }

            read = socket.Read(buffer);
            string value = Encoding.UTF8.GetString(buffer, 0, read);

            if (!value.Contains("Success"))
            {
                throw new AdbException(value);
            }
        }

        /// <inheritdoc/>
        public void InstallMultiple(DeviceData device, Stream[] splitAPKs, string packageName, params string[] arguments)
        {
            EnsureDevice(device);

            if (packageName == null)
            {
                throw new ArgumentNullException(nameof(packageName));
            }

            string session = InstallCreate(device, packageName, arguments);

            int i = 0;
            foreach (Stream splitAPK in splitAPKs)
            {
                if (splitAPK == null || !splitAPK.CanRead || !splitAPK.CanSeek)
                {
                    Debug.WriteLine("The apk stream must be a readable and seekable stream");
                    continue;
                }

                try
                {
                    InstallWrite(device, splitAPK, $"{nameof(splitAPK)}{i++}", session);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }

            InstallCommit(device, session);
        }

        /// <inheritdoc/>
        public void InstallMultiple(DeviceData device, Stream baseAPK, Stream[] splitAPKs, params string[] arguments)
        {
            EnsureDevice(device);

            if (baseAPK == null)
            {
                throw new ArgumentNullException(nameof(baseAPK));
            }

            if (!baseAPK.CanRead || !baseAPK.CanSeek)
            {
                throw new ArgumentOutOfRangeException(nameof(baseAPK), "The apk stream must be a readable and seekable stream");
            }

            string session = InstallCreate(device, null, arguments);

            InstallWrite(device, baseAPK, nameof(baseAPK), session);

            int i = 0;
            foreach (Stream splitAPK in splitAPKs)
            {
                if (splitAPK == null || !splitAPK.CanRead || !splitAPK.CanSeek)
                {
                    Debug.WriteLine("The apk stream must be a readable and seekable stream");
                    continue;
                }

                try
                {
                    InstallWrite(device, splitAPK, $"{nameof(splitAPK)}{i++}", session);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }

            InstallCommit(device, session);
        }

        /// <inheritdoc/>
        public string InstallCreate(DeviceData device, string packageName = null, params string[] arguments)
        {
            EnsureDevice(device);

            StringBuilder requestBuilder = new();
            _ = requestBuilder.Append("exec:cmd package 'install-create' ");
            _ = requestBuilder.Append(packageName.IsNullOrWhiteSpace() ? string.Empty : $"-p {packageName}");

            if (arguments != null)
            {
                foreach (string argument in arguments)
                {
                    _ = requestBuilder.Append(' ');
                    _ = requestBuilder.Append(argument);
                }
            }

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);

            socket.SendAdbRequest(requestBuilder.ToString());
            AdbResponse response = socket.ReadAdbResponse();

            using StreamReader reader = new(socket.GetShellStream(), Encoding);
            string result = reader.ReadLine();

            if (!result.Contains("Success"))
            {
                throw new AdbException(reader.ReadToEnd());
            }

            int arr = result.IndexOf("]") - 1 - result.IndexOf("[");
            string session = result.Substring(result.IndexOf("[") + 1, arr);
            return session;
        }

        /// <inheritdoc/>
        public void InstallWrite(DeviceData device, Stream apk, string apkName, string session)
        {
            EnsureDevice(device);

            if (apk == null)
            {
                throw new ArgumentNullException(nameof(apk));
            }

            if (!apk.CanRead || !apk.CanSeek)
            {
                throw new ArgumentOutOfRangeException(nameof(apk), "The apk stream must be a readable and seekable stream");
            }

            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (apkName == null)
            {
                throw new ArgumentNullException(nameof(apkName));
            }

            StringBuilder requestBuilder = new();
            requestBuilder.Append($"exec:cmd package 'install-write' ");

            // add size parameter [required for streaming installs]
            // do last to override any user specified value
            requestBuilder.Append($" -S {apk.Length}");

            requestBuilder.Append($" {session} {apkName}.apk");

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);

            socket.SendAdbRequest(requestBuilder.ToString());
            AdbResponse response = socket.ReadAdbResponse();

            byte[] buffer = new byte[32 * 1024];
            int read = 0;

            while ((read = apk.Read(buffer, 0, buffer.Length)) > 0)
            {
                socket.Send(buffer, read);
            }

            read = socket.Read(buffer);
            string value = Encoding.UTF8.GetString(buffer, 0, read);

            if (!value.Contains("Success"))
            {
                throw new AdbException(value);
            }
        }

        /// <inheritdoc/>
        public void InstallCommit(DeviceData device, string session)
        {
            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);

            socket.SendAdbRequest($"exec:cmd package 'install-commit' {session}");
            AdbResponse response = socket.ReadAdbResponse();

            using StreamReader reader = new(socket.GetShellStream(), Encoding);
            string result = reader.ReadLine();
            if (!result.Contains("Success"))
            {
                throw new AdbException(reader.ReadToEnd());
            }
        }

        /// <inheritdoc/>
        public List<string> GetFeatureSet(DeviceData device)
        {
            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SendAdbRequest($"host-serial:{device.Serial}:features");

            AdbResponse response = socket.ReadAdbResponse();
            string features = socket.ReadString();

            List<string> featureList = features.Split(new char[] { '\n', ',' }).ToList();
            return featureList;
        }

        /// <inheritdoc/>
        public XmlDocument DumpScreen(DeviceData device)
        {
            XmlDocument doc = new();
            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);
            socket.SendAdbRequest("shell:uiautomator dump /dev/tty");
            AdbResponse response = socket.ReadAdbResponse();
            using StreamReader reader = new(socket.GetShellStream(), Encoding);
            string xmlString = reader.ReadToEnd().Replace("Events injected: 1\r\n", "").Replace("UI hierchary dumped to: /dev/tty", "").Trim();
            if (xmlString != "" && !xmlString.StartsWith("ERROR"))
            {
                doc.LoadXml(xmlString);
                return doc;
            }
            return null;
        }

        /// <inheritdoc/>
        public void Click(DeviceData device, Cords cords)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);
            socket.SendAdbRequest(string.Format("shell:input tap {0} {1}", cords.X, cords.Y));
            AdbResponse response = socket.ReadAdbResponse();
            using StreamReader reader = new(socket.GetShellStream(), Encoding);
            if (reader.ReadToEnd().ToUpper().Contains("ERROR")) // error or ERROR
            {
                throw new ElementNotFoundException("Coordinates of element is invalid");
            }
        }

        /// <inheritdoc/>
        public void Click(DeviceData device, int x, int y)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);
            socket.SendAdbRequest(string.Format("shell:input tap {0} {1}", x, y));
            AdbResponse response = socket.ReadAdbResponse();
            using StreamReader reader = new(socket.GetShellStream(), Encoding);
            if (reader.ReadToEnd().ToUpper().Contains("ERROR"))
            {
                throw new ElementNotFoundException("Coordinates of element is invalid");
            }
        }

        /// <inheritdoc/>
        public void Swipe(DeviceData device, Element first, Element second, long speed)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);
            socket.SendAdbRequest(string.Format("shell:input swipe {0} {1} {2} {3} {4}", first.Cords.X, first.Cords.Y, second.Cords.X, second.Cords.Y, speed));
            AdbResponse response = socket.ReadAdbResponse();
            using StreamReader reader = new(socket.GetShellStream(), Encoding);
            if (reader.ReadToEnd().ToUpper().Contains("ERROR"))
            {
                throw new ElementNotFoundException("Coordinates of element is invalid");
            }
        }

        /// <inheritdoc/>
        public void Swipe(DeviceData device, int x1, int y1, int x2, int y2, long speed)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);
            socket.SendAdbRequest(string.Format("shell:input swipe {0} {1} {2} {3} {4}", x1, y1, x2, y2, speed));
            AdbResponse response = socket.ReadAdbResponse();
            using StreamReader reader = new(socket.GetShellStream(), Encoding);
            if (reader.ReadToEnd().ToUpper().Contains("ERROR"))
            {
                throw new ElementNotFoundException("Coordinates of element is invalid");
            }
        }

        /// <inheritdoc/>
        public bool IsCurrentApp(DeviceData device, string packageName)
        {
            ConsoleOutputReceiver receiver = new();
            ExecuteRemoteCommand($"dumpsys activity activities | grep mResumedActivity", device, receiver);
            string response = receiver.ToString().Trim();
            return response.ToString().Contains(packageName);
        }

        /// <inheritdoc/>
        public bool IsAppRunning(DeviceData device, string packageName)
        {
            ConsoleOutputReceiver receiver = new();
            ExecuteRemoteCommand($"pidof {packageName}", device, receiver);
            string response = receiver.ToString().Trim();
            bool intParsed = int.TryParse(response, out int pid);
            return intParsed && pid > 0;
        }

        /// <inheritdoc/>
        public AppStatus GetAppStatus(DeviceData device, string packageName)
        {
            // Check if the app is in foreground
            bool currentApp = IsCurrentApp(device, packageName);
            if (currentApp)
            {
                return AppStatus.Foreground;
            }

            // Check if the app is running in background
            bool isAppRunning = IsAppRunning(device, packageName);
            if (isAppRunning)
            {
                return AppStatus.Background;
            }

            return AppStatus.Stopped;
        }

        /// <inheritdoc/>
        public Element FindElement(DeviceData device, string xpath, TimeSpan timeout = default)
        {
            Stopwatch stopwatch = new();
            stopwatch.Start();
            while (timeout == TimeSpan.Zero || stopwatch.Elapsed < timeout)
            {
                XmlDocument doc = DumpScreen(device);
                if (doc != null)
                {
                    XmlNode xmlNode = doc.SelectSingleNode(xpath);
                    if (xmlNode != null)
                    {
                        string bounds = xmlNode.Attributes["bounds"].Value;
                        if (bounds != null)
                        {
                            int[] cords = bounds.Replace("][", ",").Replace("[", "").Replace("]", "").Split(',').Select(int.Parse).ToArray(); // x1, y1, x2, y2
                            Dictionary<string, string> attributes = new();
                            foreach (XmlAttribute at in xmlNode.Attributes)
                            {
                                attributes.Add(at.Name, at.Value);
                            }
                            Cords cord = new((cords[0] + cords[2]) / 2, (cords[1] + cords[3]) / 2); // Average x1, y1, x2, y2
                            return new Element(this, device, cord, attributes);
                        }
                    }
                }
                if (timeout == TimeSpan.Zero)
                {
                    break;
                }
            }
            return null;
        }

        /// <inheritdoc/>
        public Element[] FindElements(DeviceData device, string xpath, TimeSpan timeout = default)
        {
            Stopwatch stopwatch = new();
            stopwatch.Start();
            while (timeout == TimeSpan.Zero || stopwatch.Elapsed < timeout)
            {
                XmlDocument doc = DumpScreen(device);
                if (doc != null)
                {
                    XmlNodeList xmlNodes = doc.SelectNodes(xpath);
                    if (xmlNodes != null)
                    {
                        Element[] elements = new Element[xmlNodes.Count];
                        for (int i = 0; i < elements.Length; i++)
                        {
                            string bounds = xmlNodes[i].Attributes["bounds"].Value;
                            if (bounds != null)
                            {
                                int[] cords = bounds.Replace("][", ",").Replace("[", "").Replace("]", "").Split(',').Select(int.Parse).ToArray(); // x1, y1, x2, y2
                                Dictionary<string, string> attributes = new();
                                foreach (XmlAttribute at in xmlNodes[i].Attributes)
                                {
                                    attributes.Add(at.Name, at.Value);
                                }
                                Cords cord = new((cords[0] + cords[2]) / 2, (cords[1] + cords[3]) / 2); // Average x1, y1, x2, y2
                                elements[i] = new Element(this, device, cord, attributes);
                            }
                        }
                        return elements.Length == 0 ? null : elements;
                    }
                }
                if (timeout == TimeSpan.Zero)
                {
                    break;
                }
            }
            return null;
        }

        /// <inheritdoc/>
        public void SendKeyEvent(DeviceData device, string key)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);
            socket.SendAdbRequest(string.Format("shell:input keyevent {0}", key));
            AdbResponse response = socket.ReadAdbResponse();
            using StreamReader reader = new(socket.GetShellStream(), Encoding);
            if (reader.ReadToEnd().ToUpper().Contains("ERROR"))
            {
                throw new InvalidKeyEventException("KeyEvent is invalid");
            }
        }

        /// <inheritdoc/>
        public void SendText(DeviceData device, string text)
        {
            EnsureDevice(device);

            using IAdbSocket socket = adbSocketFactory(EndPoint);
            socket.SetDevice(device);
            socket.SendAdbRequest(string.Format("shell:input text {0}", text));
            AdbResponse response = socket.ReadAdbResponse();
            using StreamReader reader = new(socket.GetShellStream(), Encoding);
            if (reader.ReadToEnd().ToUpper().Contains("ERROR"))
            {
                throw new InvalidTextException();
            }
        }

        /// <inheritdoc/>
        public void ClearInput(DeviceData device, int charCount)
        {
            SendKeyEvent(device, "KEYCODE_MOVE_END");
            ExecuteRemoteCommandAsync("input keyevent " + Utilities.Join(" ", Enumerable.Repeat("KEYCODE_DEL ", charCount)), device, null, CancellationToken.None).Wait();
        }

        /// <inheritdoc/>
        public void StartApp(DeviceData device, string packageName) =>
            ExecuteRemoteCommand($"monkey -p {packageName} 1", device, null);

        /// <inheritdoc/>
        public void StopApp(DeviceData device, string packageName) =>
            ExecuteRemoteCommand($"am force-stop {packageName}", device, null);

        /// <inheritdoc/>
        public void BackBtn(DeviceData device) => SendKeyEvent(device, "KEYCODE_BACK");

        /// <inheritdoc/>
        public void HomeBtn(DeviceData device) => SendKeyEvent(device, "KEYCODE_HOME");

        /// <summary>
        /// Sets default encoding (default - UTF8)
        /// </summary>
        /// <param name="encoding"></param>
        public static void SetEncoding(Encoding encoding) => Encoding = encoding;

        /// <summary>
        /// Throws an <see cref="ArgumentNullException"/> if the <paramref name="device"/>
        /// parameter is <see langword="null"/>, and a <see cref="ArgumentOutOfRangeException"/>
        /// if <paramref name="device"/> does not have a valid serial number.
        /// </summary>
        /// <param name="device">A <see cref="DeviceData"/> object to validate.</param>
        protected void EnsureDevice(DeviceData device)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }

            if (string.IsNullOrEmpty(device.Serial))
            {
                throw new ArgumentOutOfRangeException(nameof(device), "You must specific a serial number for the device");
            }
        }
    }

    /// <summary>
    /// See as the <see cref="AdbClient"/> class.
    /// </summary>
    [Obsolete("AdvancedAdbClient is too long to remember. Please use AdbClient instead.")]
    public class AdvancedAdbClient : AdbClient
    {
    }
}
