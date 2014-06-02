using System;
using System.Linq;
using UnityEngine;

namespace RemoteTech
{
    public class ManeuverCommand : AbstractCommand
    {
        public ManeuverNode Node { get; set; }
        public double OriginalDelta { get; set; }
        public double RemainingTime { get; set; }
        public double RemainingDelta { get; set; }
        public bool EngineActivated { get; set; }
        public override int Priority { get { return 0; } }

        public override string Description
        {
            get
            {
                if (RemainingTime > 0 || RemainingDelta > 0)
                {
                    return string.Format("Executing maneuver: {0}m/s{3}Remaining duration: {1}{3}{2}", 
                        RemainingDelta.ToString("F2"), 
                        EngineActivated ? RTUtil.FormatDuration(RemainingTime) : "-:-",
                        base.Description,
                        Environment.NewLine);
                }
                return string.Format("Execute planned maneuver{0}{1}", Environment.NewLine, base.Description);
            }
        }

        public override bool Pop(FlightComputer f)
        {
            var burn = f.ActiveCommands.FirstOrDefault(c => c is BurnCommand);
            if (burn != null) {
                f.Remove (burn);
            }

            OriginalDelta = Node.DeltaV.magnitude;
            RemainingDelta = Node.GetBurnVector(f.Vessel.orbit).magnitude;
            EngineActivated = true;

            var thrustToMass = FlightCore.GetTotalThrust(f.Vessel) / f.Vessel.GetTotalMass();
            if (thrustToMass < 0.0)
            {
                RemainingTime = RemainingDelta/thrustToMass;
            }
            else
            {
                EngineActivated = false;
                RTUtil.ScreenMessage("[Flight Computer]: No engine to carry out the maneuver.");
            }

            return true;
        }

        public override bool Execute(FlightComputer f, FlightCtrlState fcs)
        {
            if (RemainingDelta > 0)
            {
                var forward = Node.GetBurnVector(f.Vessel.orbit).normalized;
                var up = (f.SignalProcessor.Body.position - f.SignalProcessor.Position).normalized;
                var orientation = Quaternion.LookRotation(forward, up);
                FlightCore.HoldOrientation(fcs, f, orientation);

                var thrustToMass = (FlightCore.GetTotalThrust(f.Vessel) / f.Vessel.GetTotalMass());
                if (thrustToMass > 0.0)
                {
                    EngineActivated = true;
                    fcs.mainThrottle = 1.0f;
                    RemainingTime = RemainingDelta/thrustToMass;
                    RemainingDelta -= thrustToMass*TimeWarp.deltaTime;
                    return false;
                }

                EngineActivated = false;
                return false;
            }
            f.Enqueue(AttitudeCommand.Off(), true, true, true);
            return true;
        }

        public static ManeuverCommand WithNode(ManeuverNode node, FlightComputer f)
        {
            var thrust = FlightCore.GetTotalThrust(f.Vessel);
            var advance = f.Delay;

            if (thrust > 0) {
                advance += (node.DeltaV.magnitude / (thrust / f.Vessel.GetTotalMass())) / 2;
            }

            var newTimeStamp = node.UT - advance;

            var newNode = new ManeuverNode
                {
                    DeltaV = node.DeltaV,
                    patch = node.patch,
                    solver = node.solver,
                    scaledSpaceTarget = node.scaledSpaceTarget,
                    nextPatch = node.nextPatch,
                    UT = node.UT,
                    nodeRotation = node.nodeRotation,
                };

            return new ManeuverCommand(){Node = newNode, TimeStamp = newTimeStamp};
        }
    }
}
