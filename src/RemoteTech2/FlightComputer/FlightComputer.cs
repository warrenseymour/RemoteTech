using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RemoteTech
{
    public class FlightComputer : IDisposable
    {
        [Flags]
        public enum State
        {
            Normal = 0,
            Packed = 2,
            OutOfPower = 4,
            NoConnection = 8,
            NotMaster = 16,
        }

        public bool InputAllowed
        {
            get
            {
                var satellite = RTCore.Instance.Network[SignalProcessor.Guid];
                var connection = RTCore.Instance.Network[satellite];
                return (satellite != null && satellite.HasLocalControl) || (SignalProcessor.Powered && connection.Any());
            }
        }

        public double Delay
        {
            get
            {
                var satellite = RTCore.Instance.Network[SignalProcessor.Guid];
                if (satellite != null && satellite.HasLocalControl) return 0.0;
                var connection = RTCore.Instance.Network[satellite];
                if (!connection.Any()) return Double.PositiveInfinity;
                return connection.Min().Delay;
            }
        }

        public State Status
        {
            get
            {
                var satellite = RTCore.Instance.Network[SignalProcessor.Guid];
                var connection = RTCore.Instance.Network[satellite];
                var status = State.Normal;
                if (!SignalProcessor.Powered) status |= State.OutOfPower;
                if (!SignalProcessor.IsMaster) status |= State.NotMaster;
                if (!connection.Any()) status |= State.NoConnection;
                if (Vessel.packed) status |= State.Packed;
                return status;
            }
        }

        public double TotalDelay { get; set; }
        public ManeuverNode DelayedManeuver { get; set; }
        public ITargetable DelayedTarget { get; set; }
        public Vessel Vessel { get; private set; }
        public ISignalProcessor SignalProcessor { get; private set; }
        public List<Action<FlightCtrlState>> SanctionedPilots { get; private set; }
        public IEnumerable<ICommand> ActiveCommands { get { return activeCommands.Values; } }
        public IEnumerable<ICommand> QueuedCommands { get { return commandQueue; } }

        private readonly SortedDictionary<int, ICommand> activeCommands = new SortedDictionary<int, ICommand>();
        private readonly List<ICommand> commandQueue = new List<ICommand>();
        private readonly PriorityQueue<DelayedFlightCtrlState> flightCtrlQueue = new PriorityQueue<DelayedFlightCtrlState>();

        // Oh .NET, why don't you have deque's?
        private readonly LinkedList<DelayedManeuver> maneuverQueue = new LinkedList<DelayedManeuver>();

        private FlightComputerWindow window;
        public FlightComputerWindow Window { get { if (window != null) window.Hide(); return window = new FlightComputerWindow(this); } }

        public FlightComputer(ISignalProcessor s)
        {
            SignalProcessor = s;
            Vessel = s.Vessel;
            SanctionedPilots = new List<Action<FlightCtrlState>>();

            var target = TargetCommand.WithTarget(FlightGlobals.fetch.VesselTarget);
            activeCommands[target.Priority] = target;
            var attitude = AttitudeCommand.Off();
            activeCommands[attitude.Priority] = attitude;
        }

        public void Dispose()
        {
            RTLog.Notify("FlightComputer: Dispose");
            if (Vessel != null)
            {
                Vessel.OnFlyByWire -= OnFlyByWirePre;
                Vessel.OnFlyByWire -= OnFlyByWirePost;
            }
            if (window != null)
            {
                window.Hide();
            }
        }

        public void Reset()
        {
            foreach (var cmd in activeCommands.Values)
            {
                cmd.Abort();
            }
        }

        public void Enqueue(ICommand cmd, bool ignoreControl = false, bool ignoreDelay = false, bool ignoreExtra = false)
        {
            if (!InputAllowed && !ignoreControl) return;

            if (!ignoreDelay) cmd.TimeStamp += Delay;
            if (!ignoreExtra) cmd.ExtraDelay += Math.Max(0, TotalDelay - Delay);

            int pos = commandQueue.BinarySearch(cmd);
            if (pos < 0)
            {
                commandQueue.Insert(~pos, cmd);
            }
        }

        public void Remove(ICommand cmd)
        {
            commandQueue.Remove(cmd);
            if (activeCommands.ContainsValue(cmd)) activeCommands.Remove(cmd.Priority);
        }

        public void OnUpdate()
        {
            if (!SignalProcessor.IsMaster) return;
            PopCommand();
        }

        public void OnFixedUpdate()
        {
            // Re-attach periodically
            Vessel.OnFlyByWire -= OnFlyByWirePre;
            Vessel.OnFlyByWire -= OnFlyByWirePost;
            if (Vessel != SignalProcessor.Vessel)
            {
                SanctionedPilots.Clear();
                Vessel = SignalProcessor.Vessel;
            }
            Vessel.OnFlyByWire = OnFlyByWirePre + Vessel.OnFlyByWire + OnFlyByWirePost;

            // Send updates for Target / Maneuver
            TargetCommand last = null;
            if (FlightGlobals.fetch.VesselTarget != DelayedTarget &&
                ((commandQueue.FindLastIndex(c => (last = c as TargetCommand) != null)) == -1 || last.Target != FlightGlobals.fetch.VesselTarget))
            {
                Enqueue(TargetCommand.WithTarget(FlightGlobals.fetch.VesselTarget));
            }

            if (Vessel.patchedConicSolver != null && Vessel.patchedConicSolver.maneuverNodes.Count > 0)
            {
                if ((DelayedManeuver == null || (Vessel.patchedConicSolver.maneuverNodes[0].DeltaV != DelayedManeuver.DeltaV)) &&
                    (maneuverQueue.Count == 0 || maneuverQueue.Last.Value.Node.DeltaV != Vessel.patchedConicSolver.maneuverNodes[0].DeltaV))
                {
                    maneuverQueue.AddLast(new DelayedManeuver(Vessel.patchedConicSolver.maneuverNodes[0]));
                }
            }

        }

        private void Enqueue(FlightCtrlState fs)
        {
            var dfs = new DelayedFlightCtrlState(fs);
            dfs.TimeStamp += Delay;
            flightCtrlQueue.Enqueue(dfs);
        }

        private void PopFlightCtrl(FlightCtrlState fcs)
        {
            var delayed = new FlightCtrlState();
            while (flightCtrlQueue.Count > 0 && flightCtrlQueue.Peek().TimeStamp <= RTUtil.GameTime)
            {
                delayed = flightCtrlQueue.Dequeue().State;
            }

            fcs.CopyFrom(delayed);
        }

        private void PopCommand()
        {
            // Maneuvers
            while (maneuverQueue.Count > 0 && maneuverQueue.First.Value.TimeStamp <= RTUtil.GameTime)
            {
                DelayedManeuver = maneuverQueue.First.Value.Node;
                maneuverQueue.RemoveFirst();
            }

            // Commands
            if (SignalProcessor.Powered && commandQueue.Count > 0)
            {
                if (RTSettings.Instance.ThrottleTimeWarp && TimeWarp.CurrentRate > 1.0f)
                {
                    var time = TimeWarp.deltaTime;
                    foreach (var dc in commandQueue.TakeWhile(c => c.TimeStamp <= RTUtil.GameTime + (2 * time + 1.0)))
                    {
                        var message = new ScreenMessage("[Flight Computer]: Throttling back time warp...", 4.0f, ScreenMessageStyle.UPPER_LEFT);
                        while ((2 * TimeWarp.deltaTime + 1.0) > (Math.Max(dc.TimeStamp - RTUtil.GameTime, 0) + dc.ExtraDelay) && TimeWarp.CurrentRate > 1.0f)
                        {
                            TimeWarp.SetRate(TimeWarp.CurrentRateIndex - 1, true);
                            ScreenMessages.PostScreenMessage(message, true);
                        }
                    }
                }

                foreach (var dc in commandQueue.TakeWhile(c => c.TimeStamp <= RTUtil.GameTime).ToList())
                {
                    Debug.Log(dc.Description);
                    if (dc.ExtraDelay > 0)
                    {
                        dc.ExtraDelay -= SignalProcessor.Powered ? TimeWarp.deltaTime : 0.0;
                    }
                    else
                    {
                        if (dc.Pop(this)) activeCommands[dc.Priority] = dc;
                        commandQueue.Remove(dc);
                    }
                }
            }
        }

        private void OnFlyByWirePre(FlightCtrlState fcs)
        {
            if (!SignalProcessor.IsMaster) return;
            var satellite = RTCore.Instance.Satellites[SignalProcessor.Guid];

            if (Vessel == FlightGlobals.ActiveVessel && InputAllowed && !satellite.HasLocalControl)
            {
                Enqueue(fcs);
            }

            if (!satellite.HasLocalControl)
            {
                PopFlightCtrl(fcs);
            }
        }

        private void OnFlyByWirePost(FlightCtrlState fcs)
        {
            if (!SignalProcessor.IsMaster) return;

            if (!InputAllowed)
            {
                fcs.Neutralize();
            }

            if (SignalProcessor.Powered)
            {
                foreach (var dc in activeCommands.Values.ToList())
                {
                    if (dc.Execute(this, fcs)) activeCommands.Remove(dc.Priority);
                }
            }

            foreach (var pilot in SanctionedPilots)
            {
                pilot.Invoke(fcs);
            }
        }
    }
}
