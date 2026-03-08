using System.Runtime.InteropServices;

namespace VAFlightTracker.Models
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct TelemetryData
    {
        public double KIAS;
        public double GroundSpeed;
        public double Altitude;
        public double VerticalSpeed;
        public double Heading;
        public double Pitch;
        public double Roll;
        public double Latitude;
        public double Longitude;
    }
}
