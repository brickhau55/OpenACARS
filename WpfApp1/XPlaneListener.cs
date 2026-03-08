using System.Net.Sockets;
using System.Text;

namespace VAFlightTracker.Models
{
    public class XPlaneListener
    {
        private UdpClient? client;
        private bool running = false;

        private TelemetryData currentTelemetry = new TelemetryData();


        public Action<string>? OnDataReceived;
        public Action<TelemetryData>? OnTelemetry;

        private DateTime lastAltTime = DateTime.UtcNow;
        private double lastAltitude = 0;

        public void Start()
        {
            if (running)
                return;

            running = true;

            OnDataReceived?.Invoke("Listener started");

            client = new UdpClient(49002);

            Task.Run(async () =>
            {
                while (running)
                {
                    try
                    {
                        var result = await client.ReceiveAsync();
                        ParsePacket(result.Buffer);
                    }
                    catch (Exception ex)
                    {
                        OnDataReceived?.Invoke(ex.Message);
                    }
                }
            });
        }

        public void Stop()
        {
            running = false;
            client?.Close();
        }

        private void ParsePacket(byte[] buffer)
        {
            if (buffer.Length < 5)
                return;

            string header = Encoding.ASCII.GetString(buffer, 0, 4).TrimEnd('\0');
            if (header != "DATA")
                return;

            int offset = 5;

            while (offset + 36 <= buffer.Length)
            {
                int index = BitConverter.ToInt32(buffer, offset);

                float v1 = BitConverter.ToSingle(buffer, offset + 4);
                float v2 = BitConverter.ToSingle(buffer, offset + 8);
                float v3 = BitConverter.ToSingle(buffer, offset + 12);
                float v4 = BitConverter.ToSingle(buffer, offset + 16);
                float v5 = BitConverter.ToSingle(buffer, offset + 20);
                float v6 = BitConverter.ToSingle(buffer, offset + 24);
                float v7 = BitConverter.ToSingle(buffer, offset + 28);
                float v8 = BitConverter.ToSingle(buffer, offset + 32);

                switch (index)
                {
                    case 3:
                        currentTelemetry.KIAS = v1;
                        currentTelemetry.GroundSpeed = (int)Math.Round(v3);
                        break;

                    case 17:
                        currentTelemetry.Pitch = v1;
                        currentTelemetry.Roll = v2;
                        currentTelemetry.Heading = v3;
                        break;

                    case 20:
                        currentTelemetry.Altitude = v3;
                        break;
                }

                offset += 36;
            }

            CalculateVerticalSpeed();

            OnTelemetry?.Invoke(currentTelemetry);
        }

        private void CalculateVerticalSpeed()
        {
            var now = DateTime.UtcNow;
            double seconds = (now - lastAltTime).TotalSeconds;

            if (seconds > 0.2)
            {
                double vs = ((currentTelemetry.Altitude - lastAltitude) / seconds) * 60;

                if (Math.Abs(vs) < 6000)
                {
                    int newVS = (int)Math.Round(vs);

                    // Smooth the value
                    currentTelemetry.VerticalSpeed =
                        (currentTelemetry.VerticalSpeed + newVS) / 2;
                }

                lastAltitude = currentTelemetry.Altitude;
                lastAltTime = now;
            }
        }
    }
}