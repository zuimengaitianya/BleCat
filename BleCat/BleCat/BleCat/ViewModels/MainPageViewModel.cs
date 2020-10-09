using Acr.UserDialogs;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.FilePicker;
using Plugin.FilePicker.Abstractions;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BleCat.ViewModels
{
    public class MainPageViewModel : ViewModelBase
    {
        public const string BleServiceUuid = "6e400001-b5a3-f393-e0a9-e50d13cbbc8a";
        public const string BleWriteCharacteristicUuid = "6e400002-b5a3-f393-e0a9-e50d13cbbc8a";
        public const string BleReadCharacteristicUuid = "6e400003-b5a3-f393-e0a9-e50d13cbbc8a";

        public const string BleDfuServiceUuid = "8e400001-f315-4f60-9fb8-838830daea50";

        public const string BleScrityServiceUuid = "0000fe59-0000-1000-8000-00805f9b34fb";
        public const string BleScrityControlPointUuid = "8EC90001-F315-4F60-9FB8-838830DAEA50";
        public const string BleScrityPackageCharUuid = "8EC90002-F315-4F60-9FB8-838830DAEA50";


        ICharacteristic notificationsCharacteristic;
        IProgressDialog progressDialog;

        Dictionary<string, byte[]> byteArrayDic = new Dictionary<string, byte[]>();
        int totalLength = 0;
        bool runDat = false;
        bool runBin = false;

        private IAdapter adapter { get; set; }
        private IBluetoothLE ble { get; set; }

        private ObservableCollection<IDevice> _foundDevices;
        public ObservableCollection<IDevice> FundDevices
        {
            get => _foundDevices;
            set => SetProperty(ref _foundDevices, value);
        }

        private IDevice _selectDevice;
        public IDevice SelectDevice
        {
            get => _selectDevice;
            set => SetProperty(ref _selectDevice, value);
        }

        public DelegateCommand ScanCommand { get; set; }
        public DelegateCommand ConnectCommand { get; set; }
        public DelegateCommand DisconnectCommand { get; set; }
        public DelegateCommand PickFileCommand { get; set; }
        public DelegateCommand SwitchDFUCommand { get; set; }
        public DelegateCommand ScanDFUCommand { get; set; }
        public DelegateCommand ConnectDFUCommand { get; set; }
        public DelegateCommand StartDFUCommand { get; set; }

        public MainPageViewModel(INavigationService navigationService)
            : base(navigationService)
        {
            try
            {
                Title = "Main Page";
                FundDevices = new ObservableCollection<IDevice>();
                ScanCommand = new DelegateCommand(ScanBluetooth);
                ConnectCommand = new DelegateCommand(ConnectBluetooth);
                DisconnectCommand = new DelegateCommand(DisconnectBluetooth);
                PickFileCommand = new DelegateCommand(PickFiles);
                SwitchDFUCommand = new DelegateCommand(SwitchDFU);
                ScanDFUCommand = new DelegateCommand(ScanDFU);
                ConnectDFUCommand = new DelegateCommand(ConnectDFU);
                StartDFUCommand = new DelegateCommand(StartDFU);

                ble = CrossBluetoothLE.Current;

                adapter = CrossBluetoothLE.Current.Adapter;
                adapter.ScanTimeout = 3000;
                adapter.DeviceConnected += ConnectedStatusHandle;
                adapter.DeviceDisconnected += DisConnectedStatusHandle;
                adapter.DeviceDiscovered += DisCoverdPeripheral;
            }
            catch (Exception ex)
            {

            }
        }
        private void ConnectedStatusHandle(object sender, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs deviceEventArgs)
        {
            Debug.WriteLine(deviceEventArgs.Device.Name + " is connected");
        }

        private void DisConnectedStatusHandle(object sender, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs deviceEventArgs)
        {
            Debug.WriteLine(deviceEventArgs.Device.Name + " is Disconnected");
        }

        private void DisCoverdPeripheral(object sender, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs deviceEventArgs)
        {
            if (!string.IsNullOrEmpty(deviceEventArgs.Device.Name))
            {
                if (!FundDevices.Contains(deviceEventArgs.Device))
                {
                    FundDevices.Add(deviceEventArgs.Device);
                }
            }
        }

        private async void ScanBluetooth()
        {
            using (UserDialogs.Instance.Loading())
            {
                FundDevices.Clear();
                await adapter.StartScanningForDevicesAsync();
            }
        }

        private async void ConnectBluetooth()
        {
            if (SelectDevice != null)
            {
                await adapter.ConnectToDeviceAsync(SelectDevice);

                var bleService = await SelectDevice.GetServiceAsync(Guid.Parse(BleServiceUuid));
                notificationsCharacteristic = await bleService.GetCharacteristicAsync(Guid.Parse(BleReadCharacteristicUuid));
                notificationsCharacteristic.ValueUpdated += (o, args) =>
                {
                    try
                    {
                        var bytes = args.Characteristic.Value;
                        string text = "";
                        for (int i = 0; i < bytes.Length; i++)
                        {
                            text += ($"0x{bytes[i].ToString("X2")},");
                        }
                        Debug.WriteLine($"********** {text.TrimEnd(',')} notify");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                    }
                };
            }
        }

        private async void DisconnectBluetooth()
        {
            if (SelectDevice != null)
            {
                if (SelectDevice.State == Plugin.BLE.Abstractions.DeviceState.Connected)
                {
                    await adapter.DisconnectDeviceAsync(SelectDevice);
                }
            }
        }

        private async void PickFiles()
        {
            using (UserDialogs.Instance.Loading())
            {

                try
                {
                    FileData fileData = await CrossFilePicker.Current.PickFile();
                    if (fileData == null)
                        return; // user canceled file picking

                    string fileName = fileData.FileName;
                    if (fileData.FileName.EndsWith(".bin"))
                    {
                        byteArrayDic.Clear();
                        byteArrayDic.Add("bin", fileData.DataArray);
                        totalLength = byteArrayDic["bin"].Length;
                    }
                    else
                    {
                        Stream firmWareStream = fileData.GetStream();
                        byteArrayDic.Clear();
                        byteArrayDic = await GetFirmwareDetailFileAsync(firmWareStream);
                    }
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine("Exception choosing file: " + ex.ToString());
                }
            }
        }
        /// <summary>
        /// Gets the firmware detail file.
        /// </summary>
        /// <returns>bin file and dat file</returns>
        private Task<Dictionary<string, byte[]>> GetFirmwareDetailFileAsync(Stream zipStream)
        {
            return Task.Run(() =>
            {
                if (zipStream == null)
                {
                    return null;
                }

                var byteArrayDic = new Dictionary<string, byte[]>();

                try
                {
                    //ZipArchive rap = ZipFile.
                    using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read, true, System.Text.Encoding.UTF8))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (entry.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                            {
                                Stream binStream = entry.Open(); // .Open will return a stream
                                byte[] binByteArray = new byte[entry.Length];
                                binStream.Read(binByteArray, 0, binByteArray.Length);
                                byteArrayDic["bin"] = binByteArray;
                            }
                            else if (entry.FullName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                            {
                                Stream datStream = entry.Open(); // .Open will return a stream
                                byte[] datByteArray = new byte[datStream.Length];
                                datStream.Read(datByteArray, 0, datByteArray.Length);
                                byteArrayDic["dat"] = datByteArray;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }

                return byteArrayDic;
            });
        }

        private async void SwitchDFU()
        {
            try
            {
                IService DFUservice = await SelectDevice.GetServiceAsync(Guid.Parse(BleDfuServiceUuid));
                var writeDFUCharacter = await DFUservice.GetCharacteristicAsync(Guid.Parse(BleDfuServiceUuid));

                writeDFUCharacter.ValueUpdated += ((o, args) =>
                {
                    var bytes = args.Characteristic.Value;
                    string text = "";
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        text += ($"0x{bytes[i].ToString("X2")},");
                    }
                    Debug.WriteLine($"**********DFU {text.TrimEnd(',')} notify");

                });
                await writeDFUCharacter.StartUpdatesAsync();

                //3 switch to DFU mode
                const byte opCodeStartDfu = 0x01;
                const byte imageType = 0x04;


                await writeDFUCharacter.WriteAsync(new byte[] { opCodeStartDfu, imageType });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private async void ScanDFU()
        {
            using (UserDialogs.Instance.Loading())
            {
                FundDevices.Clear();
                SelectDevice = null;
                await adapter.StartScanningForDevicesAsync();
            }
        }

        private async void ConnectDFU()
        {
            if (SelectDevice != null)
            {
                await adapter.ConnectToDeviceAsync(SelectDevice);
            }
        }

        private async void StartDFU()
        {
            if (SelectDevice != null)
            {
                bool onDFUmodel = false;
                var allServices = await SelectDevice.GetServicesAsync();
                foreach (var itemservice in allServices)
                {
                    if (itemservice.Id.ToString().ToLower().StartsWith("0000fe59"))
                    {
                        onDFUmodel = true;
                        break;
                    }
                }

                if (!onDFUmodel)
                    return;

                progressDialog = UserDialogs.Instance.Progress("DFU Progress", null, null, true, MaskType.Black);

                runDat = false;
                runBin = false;

                IService DFUservice = await SelectDevice.GetServiceAsync(Guid.Parse(BleScrityServiceUuid));
                var BleSecureControlPointCharacter = await DFUservice.GetCharacteristicAsync(Guid.Parse(BleScrityControlPointUuid));

                BleSecureControlPointCharacter.ValueUpdated += ((o, args) =>
                {
                    var bytes = args.Characteristic.Value;
                    string text = "";
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        text += ($"0x{bytes[i].ToString("X2")},");
                    }
                    Debug.WriteLine($"**********DFU {text.TrimEnd(',')} notify");

                    DFUNotifyHandel(DFUservice, bytes);
                });
                await BleSecureControlPointCharacter.StartUpdatesAsync();

                await BleSecureControlPointCharacter.WriteAsync(new byte[] { 0x06, 0x01 });
            }
        }

        private async void DFUNotifyHandel(IService DFUservice, byte[] resData)
        {
            try
            {
                var BleSecureControlPointCharacter = await DFUservice.GetCharacteristicAsync(Guid.Parse(BleScrityControlPointUuid));
                var BleSecurePacketCharacter = await DFUservice.GetCharacteristicAsync(Guid.Parse(BleScrityPackageCharUuid));

                byte[] resHead = new byte[3];
                Array.Copy(resData, resHead, 3);
                // received DFU data back.
                if (resHead.SequenceEqual(new byte[] { 0x60, 0x06, 0x01 }))
                {
                    if (!runDat)
                    {
                        runDat = true;
                        Debug.WriteLine($"**********send dat length{byteArrayDic["dat"].Length}");
                        byte[] imageSizeBuffer = new byte[] { 0x01, 0x01 };
                        imageSizeBuffer.ToList().AddRange(BitConverter.GetBytes(byteArrayDic["dat"].Length));

                        Debug.WriteLine($"**********send dat");
                        await BleSecureControlPointCharacter.WriteAsync(imageSizeBuffer);

                        await Task.Delay(500);

                        byte[] m_binByteArray = byteArrayDic["dat"];
                        int offset = 0;
                        const int TransmitFirmwarePacketSize = 20;
                        int totalLength = m_binByteArray.Length;
                        Debug.WriteLine($"********* dat totalLength:{totalLength}");
                        while (offset < totalLength)
                        {
                            // build packet
                            int packetLength = Math.Min(totalLength - offset, TransmitFirmwarePacketSize);
                            byte[] dataPacket = new byte[packetLength];
                            Array.Copy(m_binByteArray, offset, dataPacket, 0, packetLength);

                            string text = "";
                            for (int i = 0; i < dataPacket.Length; i++)
                            {
                                text += ($"0x{dataPacket[i].ToString("X2")},");
                            }
                            Debug.WriteLine($"********* {totalLength}dat offset:{offset}/packetLength:{packetLength}/{text} writepackage");

                            await BleSecurePacketCharacter.WriteAsync(dataPacket);
                            await Task.Delay(10);
                            // update offset
                            offset += packetLength;

                        }

                        Debug.WriteLine($"**********send 03 dat ");
                        await BleSecureControlPointCharacter.WriteAsync(new byte[] { 0x03 });

                        await Task.Delay(1000);
                        Debug.WriteLine($"**********send 04 dat ");
                        await BleSecureControlPointCharacter.WriteAsync(new byte[] { 0x04 });

                    }
                    else
                    {
                        byte[] m_binByteArray = byteArrayDic["bin"];
                        int offset = 0;
                        const int TransmitFirmwarePacketSize = 4096;
                        int totalLength = m_binByteArray.Length;
                        Debug.WriteLine($"********* bin totalLength:{totalLength}");
                        while (offset < totalLength)
                        {
                            // build packet
                            int packetLength = Math.Min(totalLength - offset, TransmitFirmwarePacketSize);
                            byte[] dataPacket = new byte[packetLength];
                            Array.Copy(m_binByteArray, offset, dataPacket, 0, packetLength);


                            Debug.WriteLine($"**********send bin 0x01 0x02 package length:{packetLength}");
                            if (packetLength == TransmitFirmwarePacketSize)
                            {
                                await BleSecureControlPointCharacter.WriteAsync(new byte[] { 0x01, 0x02, 0x00, 0x10, 0x00, 0x00 });
                            }
                            else
                            {
                                byte[] imageSizeBuffer = new byte[] { 0x01, 0x02 };
                                imageSizeBuffer.ToList().AddRange(BitConverter.GetBytes(packetLength));

                                await BleSecureControlPointCharacter.WriteAsync(imageSizeBuffer);
                            }

                            await Task.Delay(500);

                            int offsetPackage = 0;
                            const int TransmitPacketSize = 20;
                            int packageTotalLength = dataPacket.Length;
                            while (offsetPackage < packageTotalLength)
                            {
                                // build packet
                                int packetSendLength = Math.Min(packageTotalLength - offsetPackage, TransmitPacketSize);
                                byte[] sendDataPacket = new byte[packetSendLength];
                                Array.Copy(dataPacket, offsetPackage, sendDataPacket, 0, packetSendLength);

                                string text = "";
                                for (int i = 0; i < sendDataPacket.Length; i++)
                                {
                                    text += ($"0x{sendDataPacket[i].ToString("X2")},");
                                }
                                Debug.WriteLine($"*********offset: {offset} {packageTotalLength} bin offset:{offsetPackage}/packetLength:{packetSendLength}/{text} writepackage");

                                await BleSecurePacketCharacter.WriteAsync(sendDataPacket);

                                int dfuProgress = (int)(offset * 100 / totalLength);
                                Debug.WriteLine($"{dfuProgress}");
                                if (progressDialog != null)
                                    progressDialog.PercentComplete = dfuProgress;

                                await Task.Delay(10);
                                offsetPackage += packetSendLength;
                            }

                            // update offset
                            offset += packetLength;
                            await Task.Delay(500);
                            Debug.WriteLine($"**********send 03 bin ");
                            await BleSecureControlPointCharacter.WriteAsync(new byte[] { 0x03 });

                            await Task.Delay(500);
                            Debug.WriteLine($"**********send 04 bin ");
                            await BleSecureControlPointCharacter.WriteAsync(new byte[] { 0x04 });

                            await Task.Delay(500);
                        }

                        Debug.WriteLine($"**********send bin complete");

                    }
                }
                else if (resHead.SequenceEqual(new byte[] { 0x60, 0x04, 0x01 }) && resData.Length == 3)
                {
                    if (!runBin)
                    {
                        runBin = true;
                        Debug.WriteLine($"********** bin 06 02 ");

                        await Task.Delay(500);
                        await BleSecureControlPointCharacter.WriteAsync(new byte[] { 0x06, 0x02 });

                        if (progressDialog != null)
                        {
                            progressDialog.PercentComplete = 100;
                            await Task.Delay(1 * 1000);
                            progressDialog.Hide();
                        }
                    }

                }

            }
            catch (Exception ex)
            {
            }
        }

    }
}
