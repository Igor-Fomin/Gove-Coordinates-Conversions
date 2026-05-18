using System;
using System.Collections.Generic;
using DotSpatial.Projections;

namespace GoveCadGeodeticTransformer
{
    /// <summary>
    /// Mathematical engine that replicate Leica Geo Office Classical 3D transformations
    /// to bridge localized Gove AMG and Gove MGA systems.
    /// </summary>
    public class GoveGeodeticEngine
    {
        // Ellipsoid Constants
        private const double ANS_a = 6378160.0000;
        private const double ANS_rf = 298.2500000000;
        private const double GRS80_a = 6378137.0000;
        private const double GRS80_rf = 298.25722210088;

        // Custom local projection specifications for Zone 53
        private readonly ProjectionInfo _ansUtm53;
        private readonly ProjectionInfo _ansGeographic;
        private readonly ProjectionInfo _grs80Utm53;
        private readonly ProjectionInfo _grs80Geographic;

        // Gove_AMG Classical 3D Parameters (WGS84 -> ANS)
        private const double dx_AMG = 115.7583;
        private const double dy_AMG = -13.1750;
        private const double dz_AMG = -181.0146;
        private const double rx_AMG_arcsec = -1.92320;
        private const double ry_AMG_arcsec = 0.29533;
        private const double rz_AMG_arcsec = 1.58104;
        private const double sf_AMG_ppm = 2.6859;

        // Gove_MGA Classical 3D Parameters (WGS84 -> GRS80)
        private const double dx_MGA = 78.0883;
        private const double dy_MGA = 22.1961;
        private const double dz_MGA = -193.7775;
        private const double rx_MGA_arcsec = -4.11458;
        private const double ry_MGA_arcsec = -4.93375;
        private const double rz_MGA_arcsec = -2.22240;
        private const double sf_MGA_ppm = -9.2225;

        public GoveGeodeticEngine()
        {
            // Initialize projections isolating the ellipsoids without standard datum shift parameters.
            // This allows the engine to apply LGO's custom 7-parameter models manually.
            _ansUtm53 = ProjectionInfo.FromProj4String("+proj=utm +zone=53 +south +a=6378160.0 +rf=298.25 +units=m +no_defs");
            _ansGeographic = ProjectionInfo.FromProj4String("+proj=longlat +a=6378160.0 +rf=298.25 +no_defs");
            _grs80Utm53 = ProjectionInfo.FromProj4String("+proj=utm +zone=53 +south +ellps=GRS80 +units=m +no_defs");
            _grs80Geographic = ProjectionInfo.FromProj4String("+proj=longlat +ellps=GRS80 +no_defs");
        }

        /// <summary>
        /// Converts Gove AMG Grid coordinates to modern Gove MGA Grid coordinates.
        /// </summary>
        public (double Easting, double Northing, double Height) TransformAMGToMGA(double easting, double northing, double elevation)
        {
            // Step 1: Inverse Projection (Gove AMG Grid -> ANS Geodetic)
            double[] xy = { easting, northing };
            double[] z = { elevation };
            Reproject.ReprojectPoints(xy, z, _ansUtm53, _ansGeographic, 0, 1);
            double lat_ANS = xy[1] * Math.PI / 180.0;
            double lon_ANS = xy[0] * Math.PI / 180.0;
            double h_ANS = z[0];

            // Step 2: ANS Geodetic -> ANS Cartesian
            var cartesianANS = GeodeticToCartesian(lat_ANS, lon_ANS, h_ANS, ANS_a, ANS_rf);

            // Step 3: Inverse Bursa-Wolf Transformation (ANS Cartesian -> WGS84 Cartesian)
            var cartesianWGS84 = InverseBursaWolf(cartesianANS.X, cartesianANS.Y, cartesianANS.Z, 
                                                 dx_AMG, dy_AMG, dz_AMG, rx_AMG_arcsec, ry_AMG_arcsec, rz_AMG_arcsec, sf_AMG_ppm);

            // Step 4: Forward Bursa-Wolf Transformation (WGS84 Cartesian -> GRS80 Cartesian)
            var cartesianGRS80 = ForwardBursaWolf(cartesianWGS84.X, cartesianWGS84.Y, cartesianWGS84.Z, 
                                                 dx_MGA, dy_MGA, dz_MGA, rx_MGA_arcsec, ry_MGA_arcsec, rz_MGA_arcsec, sf_MGA_ppm);

            // Step 5: GRS80 Cartesian -> GRS80 Geodetic (Bowring's Method)
            var geodeticGRS80 = CartesianToGeodetic(cartesianGRS80.X, cartesianGRS80.Y, cartesianGRS80.Z, GRS80_a, GRS80_rf);

            // Step 6: Forward Projection (GRS80 Geodetic -> Gove MGA Grid)
            double[] xy_target = { geodeticGRS80.Longitude * 180.0 / Math.PI, geodeticGRS80.Latitude * 180.0 / Math.PI };
            double[] z_target = { geodeticGRS80.Height };
            Reproject.ReprojectPoints(xy_target, z_target, _grs80Geographic, _grs80Utm53, 0, 1);

            return (xy_target[0], xy_target[1], z_target[0]);
        }

        private (double X, double Y, double Z) GeodeticToCartesian(double lat, double lon, double h, double a, double rf)
        {
            double f = 1.0 / rf;
            double e2 = 2 * f - f * f;
            double V = a / Math.Sqrt(1.0 - e2 * Math.Sin(lat) * Math.Sin(lat));

            double x = (V + h) * Math.Cos(lat) * Math.Cos(lon);
            double y = (V + h) * Math.Cos(lat) * Math.Sin(lon);
            double z = (V * (1.0 - e2) + h) * Math.Sin(lat);

            return (x, y, z);
        }

        private (double Latitude, double Longitude, double Height) CartesianToGeodetic(double X, double Y, double Z, double a, double rf)
        {
            double f = 1.0 / rf;
            double e2 = 2 * f - f * f;
            double b = a * (1.0 - f);
            double ePrime2 = (a * a - b * b) / (b * b);

            double p = Math.Sqrt(X * X + Y * Y);
            double theta = Math.Atan2(Z * a, p * b);

            double lat = Math.Atan2(Z + ePrime2 * b * Math.Pow(Math.Sin(theta), 3), 
                                    p - e2 * a * Math.Pow(Math.Cos(theta), 3));
            double lon = Math.Atan2(Y, X);

            double V = a / Math.Sqrt(1.0 - e2 * Math.Sin(lat) * Math.Sin(lat));
            double h = (p / Math.Cos(lat)) - V;

            return (lat, lon, h);
        }

        private (double X, double Y, double Z) ForwardBursaWolf(double x, double y, double z, 
            double dx, double dy, double dz, double rx, double ry, double rz, double sf)
        {
            // Convert arcseconds to radians
            double thetaX = (rx / 3600.0) * (Math.PI / 180.0);
            double thetaY = (ry / 3600.0) * (Math.PI / 180.0);
            double thetaZ = (rz / 3600.0) * (Math.PI / 180.0);
            double scale = 1.0 + (sf * 1e-6);

            // CFR small angle rotation matrix multiplication
            double xRot = x + (thetaZ * y) - (thetaY * z);
            double yRot = (-thetaZ * x) + y + (thetaX * z);
            double zRot = (thetaY * x) - (thetaX * y) + z;

            double X_out = dx + scale * xRot;
            double Y_out = dy + scale * yRot;
            double Z_out = dz + scale * zRot;

            return (X_out, Y_out, Z_out);
        }

        private (double X, double Y, double Z) InverseBursaWolf(double x, double y, double z, 
            double dx, double dy, double dz, double rx, double ry, double rz, double sf)
        {
            double thetaX = (rx / 3600.0) * (Math.PI / 180.0);
            double thetaY = (ry / 3600.0) * (Math.PI / 180.0);
            double thetaZ = (rz / 3600.0) * (Math.PI / 180.0);
            double scale = 1.0 + (sf * 1e-6);

            // Shift back to geocentric origin
            double xShift = (x - dx) / scale;
            double yShift = (y - dy) / scale;
            double zShift = (z - dz) / scale;

            // CFR small angle matrix transpose (R^T) multiplication for inverse rotation
            double X_out = xShift - (thetaZ * yShift) + (thetaY * zShift);
            double Y_out = (thetaZ * xShift) + yShift - (thetaX * zShift);
            double Z_out = (-thetaY * xShift) + (thetaX * yShift) + zShift;

            return (X_out, Y_out, Z_out);
        }
    }
}
