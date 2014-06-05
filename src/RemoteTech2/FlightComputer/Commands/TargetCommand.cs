using System;

namespace RemoteTech
{
    public class TargetCommand : AbstractCommand
    {
        public override double ExtraDelay { get { return 0.0; } set {} }
        public ITargetable Target { get; set; }
        public override int Priority { get { return 1; } }

        public override String Description
        {
            get
            {
                var targetName = Target != null ? Target.GetName() : "None";
                return string.Format("Target: {0}{1}{2}", targetName, Environment.NewLine, base.Description);
            }
        }

        public override bool Pop(FlightComputer f)
        {
            f.DelayedTarget = Target;
            return true;
        }

        public override bool Execute(FlightComputer f, FlightCtrlState fcs) { return false; }

        public static TargetCommand WithTarget(ITargetable target)
        {
            return new TargetCommand
                {
                Target = target,
                TimeStamp = RTUtil.GameTime,
            };
        }
    }
}
