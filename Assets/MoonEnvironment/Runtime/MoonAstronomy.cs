using System;
using UnityEngine;

namespace Unity.MP_FPS.Moon
{
    /// <summary>
    /// Deterministic mean-orbit sky, in the DEM's east/up/north frame. This is a
    /// circular, zero-libration approximation, not a SPICE ephemeris. All clients
    /// evaluate the same authored UTC epoch plus the existing server clock.
    /// </summary>
    internal static class MoonAstronomy
    {
        public const double SynodicDays = 29.530588;
        private static readonly DateTime NewMoon = new(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
        private static readonly DateTime J2000 = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        public static void Evaluate(DateTime epoch, double hours, float latitude, float longitude,
            out Vector3 sun, out Vector3 earth, out Vector3 east, out Vector3 up,
            out Vector3 north, out float earthRotation)
        {
            double days = (epoch - J2000).TotalDays + hours / 24;
            double phase = ((epoch - NewMoon).TotalDays + hours / 24) / SynodicDays * 360;
            // Mean solar longitude, with the leading equation-of-centre terms.
            double anomaly = Radians(357.529 + .98560028 * days);
            double solarLongitude = 280.459 + .98564736 * days
                + 1.915 * Math.Sin(anomaly) + .020 * Math.Sin(2 * anomaly);
            double meridian = Radians(solarLongitude + phase + 180 + longitude);
            double lat = Radians(latitude);
            east = Equatorial(new Vector3((float)-Math.Sin(meridian), (float)Math.Cos(meridian), 0));
            up = Equatorial(new Vector3((float)(Math.Cos(lat) * Math.Cos(meridian)),
                (float)(Math.Cos(lat) * Math.Sin(meridian)), (float)Math.Sin(lat)));
            north = Equatorial(new Vector3((float)(-Math.Sin(lat) * Math.Cos(meridian)),
                (float)(-Math.Sin(lat) * Math.Sin(meridian)), (float)Math.Cos(lat)));
            Vector3 solar = Equatorial(new Vector3((float)Math.Cos(Radians(solarLongitude)),
                (float)Math.Sin(Radians(solarLongitude)), 0));
            sun = new Vector3(Vector3.Dot(solar, east), Vector3.Dot(solar, up), Vector3.Dot(solar, north));
            double lon = Radians(longitude);
            earth = new Vector3((float)-Math.Sin(lon), (float)(Math.Cos(lat) * Math.Cos(lon)),
                (float)(-Math.Sin(lat) * Math.Cos(lon)));
            earthRotation = (float)((280.46061837 + 360.98564736629 * days) % 360);
        }

        private static double Radians(double degrees) => degrees % 360 * Math.PI / 180;

        // Conventional equatorial XYZ: X=RA 0, Y=RA 6h, Z=north pole.
        private static Vector3 Equatorial(Vector3 ecliptic)
        {
            const float c = .9174821f, s = .3977772f;
            return new Vector3(ecliptic.x, ecliptic.y * c - ecliptic.z * s,
                ecliptic.y * s + ecliptic.z * c);
        }
    }
}
