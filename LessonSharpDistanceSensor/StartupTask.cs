using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using Windows.ApplicationModel.Background;
using Windows.Devices.Spi;
using Windows.Devices.Gpio;
using System.Threading.Tasks;
using Microsoft.Devices.Tpm;
using Microsoft.Azure.Devices.Client;

// The Background Application template is documented at http://go.microsoft.com/fwlink/?LinkID=533884&clcid=0x409

namespace LessonSharpDistanceSensor
{
    public sealed class StartupTask : IBackgroundTask
    {
        private SpiDevice SpiADC;
        private readonly byte[] CHANNEL_SELECTION = { 0x80, 0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0, 0xF0 }; //channels 1..8 for MCP3008
        GpioPin greenPin, redPin;

        private void initGpio()
        {
            int GREEN_LED_PIN = 35;
            int RED_LED_PIN = 47;
            var gpio = GpioController.GetDefault();
            greenPin = gpio.OpenPin(GREEN_LED_PIN);
            greenPin.SetDriveMode(GpioPinDriveMode.Output);
            redPin = gpio.OpenPin(RED_LED_PIN);
            redPin.SetDriveMode(GpioPinDriveMode.Output);
        }

        private int ReadADC(byte channel)
        {
            byte[] readBuffer = new byte[3]; // Buffer to hold read data
            byte[] writeBuffer = new byte[3] { 0x01, 0x00, 0x00 };
            writeBuffer[1] = channel;

            SpiADC.TransferFullDuplex(writeBuffer, readBuffer); // Read data from the ADC
            return ((readBuffer[1] & 3) << 8) + readBuffer[2]; //convert bytes to int
        }
        private async Task InitSPI()
        {
            try
            {
                var settings = new SpiConnectionSettings(0); // 0 maps to physical pin number 24 on the Rpi2
                settings.ClockFrequency = 500000;   // 0.5MHz clock rate
                settings.Mode = SpiMode.Mode0;      // The ADC expects idle-low clock polarity so we use Mode0

                var controller = await SpiController.GetDefaultAsync();
                SpiADC = controller.GetDevice(settings);
            }
            catch (Exception ex)
            {
                throw new Exception("SPI Initialization Failed", ex);
            }
        }
        private void initDevice()
        {
            TpmDevice device = new TpmDevice(0);
            string hubUri = device.GetHostName();
            string deviceId = device.GetDeviceId();
            string sasToken = device.GetSASToken();
            _sendDeviceClient = DeviceClient.Create(hubUri, AuthenticationMethodFactory.CreateAuthenticationWithToken(deviceId, sasToken), TransportType.Amqp);
        }
        private DeviceClient _sendDeviceClient;
        private async void SendMessages(string strMessage)
        {
            string messageString = strMessage;
            var message = new Message(Encoding.ASCII.GetBytes(messageString));
            await _sendDeviceClient.SendEventAsync(message);
        }
        public async void Run(IBackgroundTaskInstance taskInstance)
        {

            await InitSPI();
            initGpio();
            initDevice();
            while (true)
            {
                double val = ReadADC(CHANNEL_SELECTION[1]);

                val = val * (3.3 / 1024); //3,3V 
                val = 27.242 * Math.Pow(val, -1.1904); //distance in cm Sharp GP2Y0A21YK 10-80cm
                //val = 65 * Math.Pow(val, -1.1); //distance in cm Sharp GP2Y0A02 20-150cm

                SendMessages(val.ToString());
                if (val < 1024 / 3) //ADC value third low 0...341
                {
                    redPin.Write(GpioPinValue.High);
                    greenPin.Write(GpioPinValue.Low);
                }
                else if (val > 1024 * 2 / 3) //ADC value third high 683...1024
                {
                    redPin.Write(GpioPinValue.Low);
                    greenPin.Write(GpioPinValue.High);
                }
                else //ADC value in the middle 342...682
                {
                    redPin.Write(GpioPinValue.High);
                    greenPin.Write(GpioPinValue.High);
                }
                Task.Delay(1000).Wait();
            }
        }
    }
}
