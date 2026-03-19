using System;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// Telemetry Simulator - Generates realistic flight data mutations
    /// Implements simulation logic with realistic flight dynamics
    /// </summary>
    public class TelemetrySimulator
    {
        private TelemetryData currentData;
        private Random random;

        // Simulation state
        private bool armed = false;
        private bool inFlight = false;
        private FlightMode currentMode = FlightMode.STABILIZE;
        private double targetAltitude = 0;
        private double lastAltitude = 0;

        // Realistic flight parameters
        private const double ALTITUDE_CLIMB_RATE = 2.5;      // m/s
        private const double ALTITUDE_DESCENT_RATE = 1.5;    // m/s
        private const double SPEED_ACCELERATION = 0.5;       // m/s per update
        private const double SPEED_MAX = 15.0;               // m/s
        private const double SPEED_HOVER = 0.5;              // m/s in hover
        private const double BATTERY_DRAIN_ARMED = 0.05;     // % per update
        private const double BATTERY_DRAIN_FLYING = 0.15;    // % per update
        private const double FUEL_DRAIN_THROTTLE = 0.02;     // % per 10% throttle

        public TelemetrySimulator()
        {
            random = new Random();
            Reset();
        }

        /// <summary>
        /// Reset simulator to initial state
        /// </summary>
        public void Reset()
        {
            currentData = new TelemetryData
            {
                Altitude = 0,
                Speed = 0,
                Heading = 0,
                Roll = 0,
                Pitch = 0,
                Yaw = 0,
                Battery = 100,
                Voltage = 12.6,
                Current = 0,
                Satellites = 14,
                VerticalSpeed = 0,
                Rssi = -90,
                FuelRemaining = 100,
                EstimatedFlightTime = 20,
                Mode = FlightMode.STABILIZE,
                Armed = false,
                Distance = 0,
                Throttle = 0,
                GroundSpeed = 0,
                AirSpeed = 0
            };

            armed = false;
            inFlight = false;
            currentMode = FlightMode.STABILIZE;
            targetAltitude = 0;
            lastAltitude = 0;
        }

        /// <summary>
        /// Arm the aircraft
        /// </summary>
        public void Arm()
        {
            if (!armed)
            {
                armed = true;
                currentData.Armed = true;
                currentData.Throttle = 0.05;
                currentData.Current = 5.0;
            }
        }

        /// <summary>
        /// Disarm the aircraft
        /// </summary>
        public void Disarm()
        {
            if (armed && !inFlight)
            {
                armed = false;
                inFlight = false;
                currentData.Armed = false;
                currentData.Throttle = 0;
                currentData.Current = 0;
                currentData.Speed = 0;
                currentData.VerticalSpeed = 0;
            }
        }

        /// <summary>
        /// Takeoff to hover altitude
        /// </summary>
        public void Takeoff()
        {
            if (armed && !inFlight)
            {
                inFlight = true;
                SetFlightMode(FlightMode.ALT_HOLD);
                targetAltitude = 10.0;
                currentData.Throttle = 0.6;
            }
        }

        /// <summary>
        /// Land the aircraft
        /// </summary>
        public void Land()
        {
            if (inFlight)
            {
                SetFlightMode(FlightMode.LAND);
                targetAltitude = 0;
            }
        }

        /// <summary>
        /// Return to launch position
        /// </summary>
        public void ReturnToLaunch()
        {
            if (inFlight)
            {
                SetFlightMode(FlightMode.RTL);
                targetAltitude = 10.0;
            }
        }

        /// <summary>
        /// Enter loiter mode
        /// </summary>
        public void Loiter()
        {
            if (inFlight)
            {
                SetFlightMode(FlightMode.LOITER);
                currentData.Throttle = 0.5;
            }
        }

        /// <summary>
        /// Set flight mode
        /// </summary>
        public void SetFlightMode(FlightMode mode)
        {
            currentMode = mode;
            currentData.Mode = mode;
        }

        /// <summary>
        /// Update telemetry data (called every 100ms)
        /// </summary>
        public TelemetryData UpdateTelemetry()
        {
            // Update altitude
            UpdateAltitude();

            // Update speed
            UpdateSpeed();

            // Update heading
            UpdateHeading();

            // Update attitude
            UpdateAttitude();

            // Update GPS & RSSI
            UpdateGPS();

            // Update power
            UpdatePower();

            // Update vertical speed
            currentData.VerticalSpeed = (currentData.Altitude - lastAltitude) * 10; // Convert to m/s
            lastAltitude = currentData.Altitude;

            // Clamp values
            ClampValues();

            return currentData;
        }

        /// <summary>
        /// Update altitude based on current mode
        /// </summary>
        private void UpdateAltitude()
        {
            if (!armed)
            {
                currentData.Altitude = Math.Max(0, currentData.Altitude - 0.1);
                return;
            }

            if (!inFlight)
            {
                currentData.Altitude = 0;
                return;
            }

            switch (currentData.Mode)
            {
                case FlightMode.LAND:
                    if (currentData.Altitude > 0.1)
                        currentData.Altitude -= ALTITUDE_DESCENT_RATE * 0.1;
                    else
                    {
                        currentData.Altitude = 0;
                        inFlight = false;
                        currentData.Throttle = 0;
                    }
                    break;

                case FlightMode.ALT_HOLD:
                case FlightMode.LOITER:
                case FlightMode.AUTO:
                case FlightMode.POSHOLD:
                    if (Math.Abs(currentData.Altitude - targetAltitude) > 0.5)
                    {
                        if (currentData.Altitude < targetAltitude)
                            currentData.Altitude += ALTITUDE_CLIMB_RATE * 0.1;
                        else
                            currentData.Altitude -= ALTITUDE_DESCENT_RATE * 0.1;
                    }
                    break;

                default:
                    if (armed && inFlight)
                        currentData.Altitude += (currentData.Throttle - 0.5) * 0.5;
                    break;
            }

            currentData.Altitude += (random.NextDouble() - 0.5) * 0.1;
            currentData.Altitude = Math.Max(0, currentData.Altitude);
        }

        /// <summary>
        /// Update speed
        /// </summary>
        private void UpdateSpeed()
        {
            if (!inFlight)
            {
                currentData.Speed = 0;
                currentData.GroundSpeed = 0;
                currentData.AirSpeed = 0;
                return;
            }

            double targetSpeed = 0;
            switch (currentData.Mode)
            {
                case FlightMode.LOITER:
                case FlightMode.ALT_HOLD:
                case FlightMode.POSHOLD:
                    targetSpeed = SPEED_HOVER;
                    break;
                case FlightMode.STABILIZE:
                case FlightMode.AUTO:
                    targetSpeed = SPEED_MAX * currentData.Throttle;
                    break;
                case FlightMode.LAND:
                    targetSpeed = 0;
                    break;
                case FlightMode.RTL:
                    targetSpeed = 5.0;
                    break;
            }

            if (currentData.Speed < targetSpeed)
                currentData.Speed = Math.Min(targetSpeed, currentData.Speed + SPEED_ACCELERATION);
            else if (currentData.Speed > targetSpeed)
                currentData.Speed = Math.Max(targetSpeed, currentData.Speed - SPEED_ACCELERATION * 2);

            currentData.Speed += (random.NextDouble() - 0.5) * 0.3;
            currentData.Speed = Math.Max(0, currentData.Speed);

            currentData.GroundSpeed = currentData.Speed + (random.NextDouble() - 0.5) * 1.0;
            currentData.AirSpeed = currentData.Speed + (random.NextDouble() - 0.5) * 0.5;
        }

        /// <summary>
        /// Update heading
        /// </summary>
        private void UpdateHeading()
        {
            if (inFlight && currentData.Speed > 0.5)
                currentData.Heading += (random.NextDouble() - 0.5) * 5.0;
            else
                currentData.Heading += (0 - currentData.Heading) * 0.05;

            while (currentData.Heading < 0) currentData.Heading += 360;
            while (currentData.Heading >= 360) currentData.Heading -= 360;
        }

        /// <summary>
        /// Update attitude
        /// </summary>
        private void UpdateAttitude()
        {
            if (!inFlight)
            {
                currentData.Roll = 0;
                currentData.Pitch = 0;
                currentData.Yaw = currentData.Heading;
                return;
            }

            double targetPitch = (currentData.Throttle - 0.5) * 20.0;
            currentData.Pitch += (targetPitch - currentData.Pitch) * 0.1;

            double headingDelta = (random.NextDouble() - 0.5) * 10.0;
            double targetRoll = headingDelta * 2.0;
            currentData.Roll += (targetRoll - currentData.Roll) * 0.1;

            currentData.Yaw = currentData.Heading;

            currentData.Pitch = Math.Max(-45, Math.Min(45, currentData.Pitch));
            currentData.Roll = Math.Max(-45, Math.Min(45, currentData.Roll));
        }

        /// <summary>
        /// Update GPS
        /// </summary>
        private void UpdateGPS()
        {
            if (random.NextDouble() > 0.8)
            {
                currentData.Satellites = (int)(12 + (random.NextDouble() - 0.5) * 3);
                currentData.Satellites = Math.Max(3, Math.Min(20, currentData.Satellites));
            }

            double altitudeBoost = currentData.Altitude * 0.2;
            double baseRssi = -90 + altitudeBoost;
            currentData.Rssi = baseRssi + (random.NextDouble() - 0.5) * 5;
            currentData.Rssi = Math.Max(-120, Math.Min(-30, currentData.Rssi));

            if (currentData.Mode == FlightMode.RTL || currentData.Mode == FlightMode.AUTO)
                currentData.Distance = Math.Max(0, currentData.Distance - 0.5);
            else if (currentData.Mode == FlightMode.STABILIZE && inFlight)
                currentData.Distance += currentData.Speed * 0.1;
        }

        /// <summary>
        /// Update power
        /// </summary>
        private void UpdatePower()
        {
            double batteryDrain = 0;
            double fuelDrain = 0;

            if (armed)
            {
                batteryDrain = BATTERY_DRAIN_ARMED;

                if (inFlight)
                {
                    batteryDrain += BATTERY_DRAIN_FLYING;
                    fuelDrain = FUEL_DRAIN_THROTTLE * (currentData.Throttle * 100.0);
                    currentData.Current = 10.0 + (currentData.Throttle * 50.0);
                }
            }

            currentData.Battery = Math.Max(0, currentData.Battery - batteryDrain);
            currentData.Voltage = 12.6 - (100 - currentData.Battery) * 0.026;
            currentData.Voltage = Math.Max(10.0, currentData.Voltage);

            currentData.FuelRemaining = Math.Max(0, currentData.FuelRemaining - fuelDrain);

            if (inFlight && fuelDrain > 0.001)
                currentData.EstimatedFlightTime = (int)(currentData.FuelRemaining / (fuelDrain * 600));
            else
                currentData.EstimatedFlightTime = 20;
        }

        /// <summary>
        /// Clamp all values to realistic ranges
        /// </summary>
        private void ClampValues()
        {
            currentData.Altitude = Math.Max(0, currentData.Altitude);
            currentData.Speed = Math.Max(0, currentData.Speed);
            currentData.Battery = Math.Max(0, Math.Min(100, currentData.Battery));
            currentData.FuelRemaining = Math.Max(0, Math.Min(100, currentData.FuelRemaining));
            currentData.Throttle = Math.Max(0, Math.Min(1.0, currentData.Throttle));
        }

        public bool IsArmed => armed;
        public bool IsInFlight => inFlight;
        public FlightMode CurrentMode => currentMode;
    }
}
