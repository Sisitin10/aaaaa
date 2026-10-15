using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Threading;
using RealCam.WindowsHub.Services;

namespace RealCam.WindowsHub;

public partial class MainWindow : Window
{
    private readonly string _pairingCode = Random.Shared.Next(0, 1_000_000).ToString("D6");
    private readonly BridgeServer _server;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _canClose;

    public MainWindow()
    {
        InitializeComponent();
        CodeText.Text = _pairingCode;
        AddressText.Text = GetLocalAddress() ?? "No local IPv4 network";
        _server = new BridgeServer(_pairingCode);
        _statusTimer.Tick += (_, _) => UpdateStatus();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _server.StartAsync();
            HubStatus.Text = "HUB ONLINE";
            FooterStatus.Text = "LOCAL BRIDGE  ·  PORT 47821  ·  CODE " + _pairingCode;
            _statusTimer.Start();
            UpdateStatus();
        }
        catch (Exception error)
        {
            HubStatus.Text = "HUB ERROR";
            HubDot.Fill = System.Windows.Media.Brushes.OrangeRed;
            MessageBox.Show(this, "The local camera bridge could not start.\n\n" + error.Message, "RealCam Hub", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateStatus()
    {
        var game = BridgeServer.FindGame();
        if (game is null)
        {
            GameBadge.Text = "WAITING";
            ProcessState.Text = "SEARCHING";
            GameProcess.Text = "Launch Forza Horizon 6 to begin";
        }
        else
        {
            GameBadge.Text = "DETECTED";
            ProcessState.Text = "RUNNING";
            GameProcess.Text = game;
        }

        var connections = _server.ConnectedPhones;
        if (connections > 0)
        {
            HubStatus.Text = "IPHONE CONNECTED";
            HubDot.Fill = System.Windows.Media.Brushes.LimeGreen;
        }
        else
        {
            HubStatus.Text = "HUB ONLINE";
            HubDot.Fill = System.Windows.Media.Brushes.LimeGreen;
        }
    }

    private void CopyAddress_Click(object sender, RoutedEventArgs e)
    {
        var address = AddressText.Text;
        if (IPAddress.TryParse(address, out _)) System.Windows.Clipboard.SetText(address);
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_canClose) return;
        e.Cancel = true;
        _statusTimer.Stop();
        await _server.StopAsync();
        _canClose = true;
        Close();
    }

    private static string? GetLocalAddress()
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(a => a.OperationalStatus == OperationalStatus.Up)
                     .OrderBy(a => a.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1))
        {
            var address = adapter.GetIPProperties().UnicastAddresses
                .Select(u => u.Address)
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));
            if (address is not null) return address.ToString();
        }
        return null;
    }
}
