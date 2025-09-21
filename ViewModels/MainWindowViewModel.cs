using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaHex.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GUITCPClient.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private TcpClient _tcpClient = new();

    private Task _tcpClientPoller = null!;
    private CancellationTokenSource _tcpClientPollerTokenSource = null!;

    [ObservableProperty]
    private ObservableCollection<object> _logs = new();

    [ObservableProperty]
    private MemoryBinaryDocument _selectedDocument = null!;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendFileCommand))]
    private string _sourceFilepath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectToServerCommand))]
    private string _ipAddress = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectToServerCommand))]
    private string _port = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendFileCommand))]
    private bool _isConnected = false;

    [RelayCommand(CanExecute = nameof(CanConnectToServer))]
    private void ConnectToServer() {
        try {
            _tcpClient.Connect(IPAddress.Parse(IpAddress), int.Parse(Port));
            IsConnected = _tcpClient.Connected;
        } catch(SocketException e) {
            // logging
            Logs.Add(e.Message);
        }
    }

    partial void OnIsConnectedChanged(bool value)
    {
        if(!value) return;

        _tcpClientPollerTokenSource?.Dispose();
        _tcpClientPollerTokenSource = new CancellationTokenSource();
        _tcpClientPoller = Task.Factory.StartNew(async () => {
            CancellationToken ct = _tcpClientPollerTokenSource.Token;
            
            while(true) {
                
                if(ct.IsCancellationRequested) {
                    return Task.CompletedTask;
                }

                if(_tcpClient.Connected && _tcpClient.Available == 0) {
                    await Task.Delay(250, ct);
                    continue;
                }

                byte[] buf = new byte[_tcpClient.Available];
                if(buf.Length == 0) continue;
                try{
                    await _tcpClient.GetStream().ReadAsync(buf, 0, buf.Length, ct);
                    MemoryBinaryDocument mbd = new(buf, true);
                    Logs.Add(mbd);
                } catch(IOException e) {
                    Logs.Add("Ошибка при получении данных. Проверьте соединение с сервером и повторите попытку: " + e.Message);
                    Dispatcher.UIThread.Post(DisconnectFromServer);
                    return Task.CompletedTask;
                } catch(ObjectDisposedException e) {
                    Logs.Add("Соединение было разорвано. Проверьте соединение с сервером и повторите попытку: " + e.Message);
                    Dispatcher.UIThread.Post(DisconnectFromServer);
                    return Task.CompletedTask;
                }
            }
        });
    }

    private bool CanConnectToServer() {
        int _portValue = 0;
        if(!int.TryParse(Port, out _portValue)) return false;;
        return IPAddress.TryParse(IpAddress, out _) 
            && _portValue > IPEndPoint.MinPort
            && _portValue < IPEndPoint.MaxPort;
    }

    [RelayCommand]
    private void DisconnectFromServer() {
        _tcpClientPollerTokenSource.Cancel();

        _tcpClient.Close();
        _tcpClient.Dispose();

        _tcpClient = new TcpClient();
        IsConnected = false;

    }

    [RelayCommand]
    private async Task SendFromTextField(string text) {
        try{
            byte[] buf = Encoding.UTF8.GetBytes(text ?? "");
            await _tcpClient.GetStream().WriteAsync(buf, _tcpClientPollerTokenSource.Token);
        } catch(Exception e) {
            Logs.Add(e.Message + Environment.NewLine + e.InnerException.Message);
            DisconnectFromServer();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSendFile))]
    private async Task SendFile() {
        if(!File.Exists(SourceFilepath)) return;
        
        try{
            await File.OpenRead(SourceFilepath).CopyToAsync(_tcpClient.GetStream());
        } catch(Exception e) {
            Logs.Add(e.Message);
            DisconnectFromServer();
        }
    }

    private bool CanSendFile() {
        return IsConnected && !string.IsNullOrWhiteSpace(SourceFilepath) && File.Exists(SourceFilepath) && !_tcpClientPollerTokenSource.IsCancellationRequested;
    }

    public Func<FilePickerOpenOptions, Task<IReadOnlyList<IStorageFile>>>? SelectFileFromFilesystem = null;
    public Func<string, Task<IStorageFile?>>? SelectFileFromPath = null;

    [RelayCommand]
    private async Task SelectFile() {
        if(SelectFileFromFilesystem == null) {
            Logs.Add("Невозможно открыть окно выбора файла на данной платформе");
            return;
        }

        var buf = await SelectFileFromFilesystem(new FilePickerOpenOptions() { AllowMultiple = false });
        if(buf.Count == 0) { SourceFilepath = ""; return; }
        SourceFilepath = buf[0].Path.AbsolutePath ?? "";
    }
}
