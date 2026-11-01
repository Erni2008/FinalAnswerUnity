using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FinalAnswer.Application;

namespace FinalAnswer.Infrastructure
{
    /// <summary>
    /// Development/solo hosting: launches the unchanged Java RoomServer from the repository's Gradle
    /// installDist output and stops only the process it started (NETWORK_ARCHITECTURE §1).
    /// A release build will point this at a bundled JRE + server JAR instead.
    /// </summary>
    public sealed class LocalServerProcessHost : IServerProcessHost, IDisposable
    {
        readonly string repositoryRoot;
        Process process;

        public LocalServerProcessHost(string repositoryRoot)
        {
            this.repositoryRoot = repositoryRoot;
        }

        public string LauncherPath => Path.Combine(repositoryRoot ?? "", "server", "build", "install", "server", "bin",
            Environment.OSVersion.Platform == PlatformID.Win32NT ? "server.bat" : "server");

        public bool IsAvailable => !string.IsNullOrEmpty(repositoryRoot) && File.Exists(LauncherPath);
        public bool IsRunning => process != null && !process.HasExited;
        public string Status { get; private set; } = "";

        public void Start(int port)
        {
            if (IsRunning) return;
            if (!IsAvailable)
            {
                Status = "Нет сборки сервера. Выполните: ./gradlew :server:installDist";
                return;
            }
            if (!PortIsFree(port))
            {
                // Never attach to an unknown service on a busy port.
                Status = "Порт " + port + " занят другим процессом";
                return;
            }
            var info = new ProcessStartInfo(LauncherPath, port.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            try
            {
                process = Process.Start(info);
                process.OutputDataReceived += (_, __) => { };
                process.ErrorDataReceived += (_, __) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                Status = "Сервер запускается…";
            }
            catch (Exception error)
            {
                process = null;
                Status = "Не удалось запустить сервер: " + error.Message;
            }
        }

        public void Stop()
        {
            if (process == null) return;
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch (Exception) { /* already exited */ }
            process.Dispose();
            process = null;
            Status = "Сервер остановлен";
        }

        public void Dispose() => Stop();

        static bool PortIsFree(int port)
        {
            foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                if (endpoint.Port == port) return false;
            return true;
        }

        /// <summary>Every private IPv4 address of an up interface with its adapter name (phone network choices).</summary>
        public static System.Collections.Generic.List<(string label, string ip)> LanAddresses()
        {
            var result = new System.Collections.Generic.List<(string, string)>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var address in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var b = address.Address.GetAddressBytes();
                        if (b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                            result.Add((nic.Name + " · LAN", address.Address.ToString()));
                    }
                }
            }
            catch (Exception) { /* no interfaces */ }
            return result;
        }

        /// <summary>First private IPv4 address of an up interface, for the phone URL. 127.0.0.1 is only for this machine.</summary>
        public static string LanAddress()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var address in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var bytes = address.Address.GetAddressBytes();
                        var isPrivate = bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31;
                        if (isPrivate) return address.Address.ToString();
                    }
                }
            }
            catch (Exception) { /* fall through */ }
            return IPAddress.Loopback.ToString();
        }
    }

    public sealed class StopwatchClock : IMonotonicClock
    {
        readonly Stopwatch watch = Stopwatch.StartNew();
        public long NowMs => watch.ElapsedMilliseconds;
    }
}
