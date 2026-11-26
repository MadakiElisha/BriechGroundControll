using System;
using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// Decides how the main header bar fits its width. The bar holds, left to right: the navigation tabs, the
    /// vehicle-state strip (arm, mode, GPS, pinned modes) and, on the right, the Briech logo, the connection
    /// settings and Connect.
    ///
    /// One owner decides everything, in a fixed order of what gives way first, so the items never compete for
    /// space among themselves (which is how the logo used to end up hidden): the bar degrades step by step
    /// until it fits, and whatever is left over goes to the vehicle-state strip's extras. Nothing the pilot
    /// needs is ever pushed into the toolbar's hidden overflow; the logo shrinks to the shield mark at worst.
    /// Pure logic so every width is unit tested.
    /// </summary>
    public static class HeaderLayoutPlanner
    {
        public enum NavStyle
        {
            Words,
            TightWords,
            Icons,
            NarrowIcons
        }

        public enum LogoStyle
        {
            Full,     // 200 x 31
            Compact,  // 148 x 23
            Small,    // 116 x 18
            Mark      // the shield on its own
        }

        public enum VehicleStateStyle
        {
            Full,     // arm + mode + GPS + pinned modes, as many as fit
            Minimal,  // arm + mode only
            Hidden    // the screen shows arm and mode itself (the Fly screen does)
        }

        /// <summary>Widths of everything on the bar, including each item's margins.</summary>
        public class Inputs
        {
            public int Available;
            /// <summary>Word widths of the nav tabs (text plus padding), in order.</summary>
            public IList<int> NavWordWidths = new List<int>();
            public int NavMargin = 4;
            public int VehicleMinimal;       // arm + mode
            public int VehicleGps;           // GPS block, when shown
            public IList<int> VehiclePinned = new List<int>(); // pinned mode buttons, in order
            public bool VehicleStateWanted = true;
            public int VehicleMargin = 18;
            public int LogoMargin = 18;
            public int ConnectionFull = 241, ConnectionCompact = 200, ConnectionMargin = 18;
            public int ConnectFull = 112, ConnectCompact = 48, ConnectMargin = 8;
            /// <summary>Kept free for the strip's own gaps and rounding.</summary>
            public int Slack = 12;
        }

        public class Plan
        {
            public NavStyle Nav;
            public LogoStyle Logo;
            public VehicleStateStyle Vehicle;
            /// <summary>Width given to the vehicle-state strip (0 when hidden).</summary>
            public int VehicleBudget;
            public bool VehicleShowsGps;
            public int VehiclePinnedShown;
            public bool ConnectionCompact;
            public bool ConnectCompact;
            public int Used;
            public bool Fits;
            /// <summary>Which degradation step was reached (0 = everything full).</summary>
            public int Step;

            public override string ToString()
            {
                return $"step {Step}: nav {Nav}, logo {Logo}, vehicle {Vehicle} ({VehicleBudget}px, gps {VehicleShowsGps}, pinned {VehiclePinnedShown}), " +
                       $"connection {(ConnectionCompact ? "compact" : "full")}, connect {(ConnectCompact ? "compact" : "full")}, used {Used}{(Fits ? "" : " (does not fit)")}";
            }
        }

        public static int LogoWidth(LogoStyle s)
        {
            switch (s)
            {
                case LogoStyle.Full: return 210;
                case LogoStyle.Compact: return 158;
                case LogoStyle.Small: return 126;
                default: return 40;
            }
        }

        public static int NavWidth(int wordWidth, NavStyle s)
        {
            switch (s)
            {
                case NavStyle.Words: return wordWidth;
                case NavStyle.TightWords: return Math.Max(44, wordWidth - 20);
                case NavStyle.Icons: return 52;
                default: return 44;
            }
        }

        // what gives way first, in order; each entry changes one thing
        private static readonly Action<Plan>[] Steps =
        {
            p => p.ConnectionCompact = true,
            p => p.Nav = NavStyle.TightWords,
            p => p.Vehicle = VehicleStateStyle.Minimal,
            p => p.Logo = LogoStyle.Compact,
            p => p.Nav = NavStyle.Icons,
            p => p.Logo = LogoStyle.Small,
            p => p.Vehicle = VehicleStateStyle.Hidden,
            p => p.Nav = NavStyle.NarrowIcons,
            p => p.Logo = LogoStyle.Mark,
            p => p.ConnectCompact = true,
        };

        public static int StepCount => Steps.Length;

        public static Plan Decide(Inputs x)
        {
            Plan best = null;
            for (int step = 0; step <= Steps.Length; step++)
            {
                var p = new Plan
                {
                    Nav = NavStyle.Words,
                    Logo = LogoStyle.Full,
                    Vehicle = x.VehicleStateWanted ? VehicleStateStyle.Full : VehicleStateStyle.Hidden,
                    Step = step
                };
                for (int i = 0; i < step; i++)
                    Steps[i](p);
                if (!x.VehicleStateWanted)
                    p.Vehicle = VehicleStateStyle.Hidden;

                p.Used = Fixed(x, p) + (p.Vehicle == VehicleStateStyle.Hidden ? 0 : x.VehicleMinimal + x.VehicleMargin);
                best = p;
                if (p.Used + x.Slack <= x.Available)
                {
                    p.Fits = true;
                    break;
                }
            }

            // whatever is left goes to the vehicle-state strip: GPS first, then pinned modes in order
            if (best.Vehicle != VehicleStateStyle.Hidden)
            {
                int spare = Math.Max(0, x.Available - x.Slack - best.Used);
                int budget = x.VehicleMinimal;
                if (best.Vehicle == VehicleStateStyle.Full)
                {
                    if (x.VehicleGps > 0 && spare >= x.VehicleGps)
                    {
                        best.VehicleShowsGps = true;
                        budget += x.VehicleGps;
                        spare -= x.VehicleGps;
                    }

                    foreach (var w in x.VehiclePinned)
                    {
                        if (spare < w)
                            break;
                        best.VehiclePinnedShown++;
                        budget += w;
                        spare -= w;
                    }
                }

                best.VehicleBudget = budget;
                best.Used += budget - x.VehicleMinimal;
            }

            return best;
        }

        private static int Fixed(Inputs x, Plan p)
        {
            int nav = x.NavWordWidths.Sum(w => NavWidth(w, p.Nav) + x.NavMargin);
            int logo = LogoWidth(p.Logo) + x.LogoMargin;
            int connection = (p.ConnectionCompact ? x.ConnectionCompact : x.ConnectionFull) + x.ConnectionMargin;
            int connect = (p.ConnectCompact ? x.ConnectCompact : x.ConnectFull) + x.ConnectMargin;
            return nav + logo + connection + connect;
        }
    }
}
