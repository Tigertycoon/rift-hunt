using System;
using UnityEngine;

namespace StoryMint.RiftHunt
{
    public enum StoryMintLocationPermissionState
    {
        Unknown,
        Prompt,
        Granted,
        Denied,
        Unsupported
    }

    public enum StoryMintLocationStatus
    {
        Idle,
        RequestingPermission,
        Ready,
        Watching,
        Mock,
        PermissionDenied,
        Unavailable,
        Timeout,
        Error
    }

    [Serializable]
    public struct StoryMintGeoCoordinate
    {
        public double latitude;
        public double longitude;

        public StoryMintGeoCoordinate(double latitude, double longitude)
        {
            this.latitude = latitude;
            this.longitude = longitude;
        }

        public bool IsValid
        {
            get
            {
                return latitude >= -90d
                    && latitude <= 90d
                    && longitude >= -180d
                    && longitude <= 180d
                    && !(Math.Abs(latitude) < double.Epsilon && Math.Abs(longitude) < double.Epsilon);
            }
        }
    }

    [Serializable]
    public struct StoryMintLocationFix
    {
        public StoryMintGeoCoordinate coordinate;
        public float accuracyMeters;
        public bool hasAltitude;
        public double altitudeMeters;
        public bool hasHeading;
        public double headingDegrees;
        public bool hasSpeed;
        public double speedMetersPerSecond;
        public double timestampSeconds;
        public bool isMock;

        public bool IsUsable
        {
            get { return coordinate.IsValid; }
        }
    }

    [Serializable]
    public struct StoryMintGeofenceResult
    {
        public bool targetIsValid;
        public bool hasFix;
        public bool isInside;
        public bool accuracyAcceptable;
        public double distanceMeters;
        public float radiusMeters;
        public float accuracyMeters;
        public string message;

        public string BuildStatusText(string locationName)
        {
            if (!targetIsValid)
                return "Dieser Story-Ort hat noch keine gueltige GPS-Position.";

            if (!hasFix)
                return "GPS wird gesucht. Standortfreigabe und HTTPS sind erforderlich.";

            if (!accuracyAcceptable)
                return "GPS ist noch ungenau (" + Mathf.CeilToInt(accuracyMeters) + " m). Bleib kurz stehen.";

            if (isInside)
                return string.IsNullOrWhiteSpace(locationName) ? "Story-Ort erreicht." : locationName + " erreicht.";

            int remainingMeters = Mathf.CeilToInt((float)Math.Max(0d, distanceMeters - radiusMeters));
            return "Noch ca. " + remainingMeters + " m bis zum Story-Ort.";
        }
    }

    public static class StoryMintGeoMath
    {
        private const double EarthRadiusMeters = 6371000d;

        public static double DistanceMeters(StoryMintGeoCoordinate from, StoryMintGeoCoordinate to)
        {
            double latitudeDelta = DegreesToRadians(to.latitude - from.latitude);
            double longitudeDelta = DegreesToRadians(to.longitude - from.longitude);
            double fromLatitude = DegreesToRadians(from.latitude);
            double toLatitude = DegreesToRadians(to.latitude);

            double a = Math.Sin(latitudeDelta / 2d) * Math.Sin(latitudeDelta / 2d)
                + Math.Cos(fromLatitude) * Math.Cos(toLatitude)
                * Math.Sin(longitudeDelta / 2d) * Math.Sin(longitudeDelta / 2d);
            double c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
            return EarthRadiusMeters * c;
        }

        public static StoryMintGeofenceResult EvaluateGeofence(
            StoryMintLocationFix fix,
            bool hasFix,
            StoryMintGeoCoordinate target,
            float radiusMeters,
            float requiredAccuracyMeters)
        {
            StoryMintGeofenceResult result = new StoryMintGeofenceResult
            {
                targetIsValid = target.IsValid,
                hasFix = hasFix,
                radiusMeters = Mathf.Max(1f, radiusMeters),
                accuracyMeters = fix.accuracyMeters,
                accuracyAcceptable = true,
                message = string.Empty
            };

            if (!result.targetIsValid || !hasFix || !fix.IsUsable)
            {
                result.accuracyAcceptable = false;
                return result;
            }

            result.distanceMeters = DistanceMeters(fix.coordinate, target);
            result.accuracyAcceptable = requiredAccuracyMeters <= 0f
                || fix.accuracyMeters <= 0f
                || fix.accuracyMeters <= requiredAccuracyMeters;
            result.isInside = result.accuracyAcceptable && result.distanceMeters <= result.radiusMeters;
            return result;
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180d;
        }
    }
}
