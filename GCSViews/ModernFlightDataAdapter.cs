using System;
using System.Windows.Forms;
using log4net;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// Integration Adapter - Makes ModernFlightDataComplete compatible with MainV2
    /// Bridges the new complete implementation with the existing screen system
    /// Inherits from MyUserControl to integrate with MainSwitcher navigation
    /// </summary>
    public partial class ModernFlightDataAdapter : MyUserControl
    {
        private ModernFlightDataComplete _modernFlightDataComplete;
        private static readonly ILog log = 
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public ModernFlightDataAdapter()
        {
            try
            {
                // Initialize the complete implementation
                _modernFlightDataComplete = new ModernFlightDataComplete();

                // Configure as UserControl
                this.Dock = DockStyle.Fill;
                this.BackColor = BriechTheme.BRIECH_DARK;
                this.Controls.Add(_modernFlightDataComplete);

                log.Info("ModernFlightDataAdapter initialized successfully");
            }
            catch (Exception ex)
            {
                log.Error("Failed to initialize ModernFlightDataAdapter: " + ex.Message, ex);
                throw;
            }
        }

        /// <summary>
        /// Get the underlying complete implementation
        /// </summary>
        public ModernFlightDataComplete GetCompleteImplementation()
        {
            return _modernFlightDataComplete;
        }

        /// <summary>
        /// Start telemetry simulation (for testing)
        /// </summary>
        public void StartSimulation()
        {
            _modernFlightDataComplete?.StartSimulation();
        }

        /// <summary>
        /// Stop telemetry simulation
        /// </summary>
        public void StopSimulation()
        {
            _modernFlightDataComplete?.StopSimulation();
        }

        /// <summary>
        /// Get current telemetry snapshot
        /// </summary>
        public TelemetryData GetTelemetry()
        {
            return _modernFlightDataComplete?.GetCurrentTelemetry();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _modernFlightDataComplete?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
