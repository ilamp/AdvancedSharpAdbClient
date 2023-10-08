﻿// <copyright file="DeviceExtensions.cs" company="The Android Open Source Project, Ryan Conrad, Quamotion, yungd1plomat, wherewhere">
// Copyright (c) The Android Open Source Project, Ryan Conrad, Quamotion, yungd1plomat, wherewhere. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AdvancedSharpAdbClient.DeviceCommands
{
    /// <summary>
    /// Provides extension methods for the <see cref="DeviceData"/> class,
    /// allowing you to run commands directory against a <see cref="DeviceData"/> object.
    /// </summary>
    public static partial class DeviceExtensions
    {
        /// <summary>
        /// Executes a shell command on the device.
        /// </summary>
        /// <param name="client">The <see cref="IAdbClient"/> to use when executing the command.</param>
        /// <param name="device">The device on which to run the command.</param>
        /// <param name="command">The command to execute.</param>
        public static void ExecuteShellCommand(this IAdbClient client, DeviceData device, string command) =>
            client.ExecuteRemoteCommand(command, device, AdbClient.Encoding);

        /// <summary>
        /// Executes a shell command on the device.
        /// </summary>
        /// <param name="client">The <see cref="IAdbClient"/> to use when executing the command.</param>
        /// <param name="device">The device on which to run the command.</param>
        /// <param name="command">The command to execute.</param>
        /// <param name="receiver">Optionally, a <see cref="IShellOutputReceiver"/> that processes the command output.</param>
        public static void ExecuteShellCommand(this IAdbClient client, DeviceData device, string command, IShellOutputReceiver receiver) =>
            client.ExecuteRemoteCommand(command, device, receiver, AdbClient.Encoding);

        /// <summary>
        /// Gets the file statistics of a given file.
        /// </summary>
        /// <param name="client">The <see cref="IAdbClient"/> to use when executing the command.</param>
        /// <param name="device">The device on which to look for the file.</param>
        /// <param name="path">The path to the file.</param>
        /// <returns>A <see cref="FileStatistics"/> object that represents the file.</returns>
        public static FileStatistics Stat(this IAdbClient client, DeviceData device, string path)
        {
            using ISyncService service = Factories.SyncServiceFactory(client, device);
            return service.Stat(path);
        }

        /// <summary>
        /// Lists the contents of a directory on the device.
        /// </summary>
        /// <param name="client">The <see cref="IAdbClient"/> to use when executing the command.</param>
        /// <param name="device">The device on which to list the directory.</param>
        /// <param name="remotePath">The path to the directory on the device.</param>
        /// <returns>For each child item of the directory, a <see cref="FileStatistics"/> object with information of the item.</returns>
        public static IEnumerable<FileStatistics> List(this IAdbClient client, DeviceData device, string remotePath)
        {
            using ISyncService service = Factories.SyncServiceFactory(client, device);
            return service.GetDirectoryListing(remotePath);
        }

        /// <summary>
        /// Pulls (downloads) a file from the remote device.
        /// </summary>
        /// <param name="client">The <see cref="IAdbClient"/> to use when executing the command.</param>
        /// <param name="device">The device on which to pull the file.</param>
        /// <param name="remotePath">The path, on the device, of the file to pull.</param>
        /// <param name="stream">A <see cref="Stream"/> that will receive the contents of the file.</param>
        /// <param name="syncProgressEventHandler">An optional handler for the <see cref="ISyncService.SyncProgressChanged"/> event.</param>
        /// <param name="progress">An optional parameter which, when specified, returns progress notifications. The progress is reported as a value between 0 and 100, representing the percentage of the file which has been transferred.</param>
        /// <param name="isCancelled">A <see cref="bool"/> that can be used to cancel the task.</param>
        public static void Pull(this IAdbClient client, DeviceData device,
            string remotePath, Stream stream,
            EventHandler<SyncProgressChangedEventArgs> syncProgressEventHandler = null,
            IProgress<int> progress = null,
            in bool isCancelled = false)
        {
            using ISyncService service = Factories.SyncServiceFactory(client, device);
            if (syncProgressEventHandler != null)
            {
                service.SyncProgressChanged += syncProgressEventHandler;
            }
            service.Pull(remotePath, stream, progress, in isCancelled);
        }

        /// <summary>
        /// Pushes (uploads) a file to the remote device.
        /// </summary>
        /// <param name="client">The <see cref="IAdbClient"/> to use when executing the command.</param>
        /// <param name="device">The device on which to put the file.</param>
        /// <param name="remotePath">The path, on the device, to which to push the file.</param>
        /// <param name="stream">A <see cref="Stream"/> that contains the contents of the file.</param>
        /// <param name="permissions">The permission octet that contains the permissions of the newly created file on the device.</param>
        /// <param name="timestamp">The time at which the file was last modified.</param>
        /// <param name="syncProgressEventHandler">An optional handler for the <see cref="ISyncService.SyncProgressChanged"/> event.</param>
        /// <param name="progress">An optional parameter which, when specified, returns progress notifications. The progress is reported as a value between 0 and 100, representing the percentage of the file which has been transferred.</param>
        /// <param name="isCancelled">A <see cref="bool"/> that can be used to cancel the task.</param>
        public static void Push(this IAdbClient client, DeviceData device,
            string remotePath, Stream stream, int permissions, DateTimeOffset timestamp,
            EventHandler<SyncProgressChangedEventArgs> syncProgressEventHandler = null,
            IProgress<int> progress = null,
            in bool isCancelled = false)
        {
            using ISyncService service = Factories.SyncServiceFactory(client, device);
            if (syncProgressEventHandler != null)
            {
                service.SyncProgressChanged += syncProgressEventHandler;
            }
            service.Push(stream, remotePath, permissions, timestamp, progress, in isCancelled);
        }

        /// <summary>
        /// Gets the property of a device.
        /// </summary>
        /// <param name="client">The connection to the adb server.</param>
        /// <param name="device">The device for which to get the property.</param>
        /// <param name="property">The name of property which to get.</param>
        /// <returns>The value of the property on the device.</returns>
        public static string GetProperty(this IAdbClient client, DeviceData device, string property)
        {
            ConsoleOutputReceiver receiver = new();
            client.ExecuteShellCommand(device, $"{GetPropReceiver.GetPropCommand} {property}", receiver);
            return receiver.ToString();
        }

        /// <summary>
        /// Gets the properties of a device.
        /// </summary>
        /// <param name="client">The connection to the adb server.</param>
        /// <param name="device">The device for which to list the properties.</param>
        /// <returns>A dictionary containing the properties of the device, and their values.</returns>
        public static Dictionary<string, string> GetProperties(this IAdbClient client, DeviceData device)
        {
            GetPropReceiver receiver = new();
            client.ExecuteShellCommand(device, GetPropReceiver.GetPropCommand, receiver);
            return receiver.Properties;
        }

        /// <summary>
        /// Gets the environment variables currently defined on a device.
        /// </summary>
        /// <param name="client">The connection to the adb server.</param>
        /// <param name="device">The device for which to list the environment variables.</param>
        /// <returns>A dictionary containing the environment variables of the device, and their values.</returns>
        public static Dictionary<string, string> GetEnvironmentVariables(this IAdbClient client, DeviceData device)
        {
            EnvironmentVariablesReceiver receiver = new();
            client.ExecuteShellCommand(device, EnvironmentVariablesReceiver.PrintEnvCommand, receiver);
            return receiver.EnvironmentVariables;
        }

        /// <summary>
        /// Uninstalls a package from the device.
        /// </summary>
        /// <param name="client">The connection to the adb server.</param>
        /// <param name="device">The device on which to uninstall the package.</param>
        /// <param name="packageName">The name of the package to uninstall.</param>
        public static void UninstallPackage(this IAdbClient client, DeviceData device, string packageName)
        {
            PackageManager manager = new(client, device);
            manager.UninstallPackage(packageName);
        }

        /// <summary>
        /// Requests the version information from the device.
        /// </summary>
        /// <param name="client">The connection to the adb server.</param>
        /// <param name="device">The device on which to uninstall the package.</param>
        /// <param name="packageName">The name of the package from which to get the application version.</param>
        public static VersionInfo GetPackageVersion(this IAdbClient client, DeviceData device, string packageName)
        {
            PackageManager manager = new(client, device);
            return manager.GetVersionInfo(packageName);
        }

        /// <summary>
        /// Lists all processes running on the device.
        /// </summary>
        /// <param name="client">A connection to ADB.</param>
        /// <param name="device">The device on which to list the processes that are running.</param>
        /// <returns>An <see cref="IEnumerable{AndroidProcess}"/> that will iterate over all processes
        /// that are currently running on the device.</returns>
        public static IEnumerable<AndroidProcess> ListProcesses(this IAdbClient client, DeviceData device)
        {
            // There are a couple of gotcha's when listing processes on an Android device.
            // One way would be to run ps and parse the output. However, the output of
            // ps different from Android version to Android version, is not delimited, nor
            // entirely fixed length, and some of the fields can be empty, so it's almost impossible
            // to parse correctly.
            //
            // The alternative is to directly read the values in /proc/[pid], pretty much like ps
            // does (see https://android.googlesource.com/platform/system/core/+/master/toolbox/ps.c).
            //
            // The easiest way to do the directory listings would be to use the SyncService; unfortunately,
            // the sync service doesn't work very well with /proc/ so we're back to using ls and taking it
            // from there.
            List<AndroidProcess> processes = [];

            // List all processes by doing ls /proc/.
            // All subfolders which are completely numeric are PIDs

            // Android 7 and above ships with toybox (https://github.com/landley/toybox), which includes
            // an updated ls which behaves slightly different.
            // The -1 parameter is important to make sure each item gets its own line (it's an assumption we
            // make when parsing output); on Android 7 and above we may see things like:
            // 1     135   160   171 ioports      timer_stats
            // 10    13533 16056 172 irq tty
            // 100   136   16066 173 kallsyms uid_cputime
            // but unfortunately older versions do not handle the -1 parameter well. So we need to branch based
            // on the API level. We do the branching on the device (inside a shell script) to avoid roundtrips.
            // This if/then/else syntax was tested on Android 2.x, 4.x and 7
            ConsoleOutputReceiver receiver = new();
            client.ExecuteShellCommand(device, @"SDK=""$(/system/bin/getprop ro.build.version.sdk)""
if [ $SDK -lt 24 ]
then
    /system/bin/ls /proc/
else
    /system/bin/ls -1 /proc/
fi".Replace("\r\n", "\n"), receiver);

            List<int> pids = [];

            string output = receiver.ToString();
            using (StringReader reader = new(output))
            {
                while (reader.Peek() > 0)
                {
                    string line = reader.ReadLine();

                    if (!line.All(char.IsDigit))
                    {
                        continue;
                    }

                    int pid = int.Parse(line);

                    pids.Add(pid);
                }
            }

            // For each pid, we can get /proc/[pid]/stat, which contains the process information in a well-defined
            // format - see http://man7.org/linux/man-pages/man5/proc.5.html.
            // Doing cat on each file one by one takes too much time. Doing cat on all of them at the same time doesn't work
            // either, because the command line would be too long.
            // So we do it 25 processes at at time.
            StringBuilder catBuilder = new();
            ProcessOutputReceiver processOutputReceiver = new();

            _ = catBuilder.Append("cat ");

            for (int i = 0; i < pids.Count; i++)
            {
                _ = catBuilder.Append($"/proc/{pids[i]}/cmdline /proc/{pids[i]}/stat ");

                if (i > 0 && (i % 25 == 0 || i == pids.Count - 1))
                {
                    client.ExecuteShellCommand(device, catBuilder.ToString(), processOutputReceiver);
                    catBuilder.Clear();
                    _ = catBuilder.Append("cat ");
                }
            }

            processOutputReceiver.Flush();

            return processOutputReceiver.Processes;
        }
    }
}
