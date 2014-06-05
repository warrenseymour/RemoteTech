using System;

namespace RemoteTech
{
    public class ActionGroupCommand : AbstractCommand
    {
        public KSPActionGroup ActionGroup { get; private set; }

        public override string Description
        {
            get { return string.Format("Toggle {0}{1}{2}", ActionGroup, Environment.NewLine, base.Description); }
        }

        public override bool Pop(FlightComputer f)
        {
            f.Vessel.ActionGroups.ToggleGroup(ActionGroup);
            if (ActionGroup == KSPActionGroup.Stage && !(f.Vessel == FlightGlobals.ActiveVessel && FlightInputHandler.fetch.stageLock))
            {
                Staging.ActivateNextStage();
                ResourceDisplay.Instance.Refresh();
            }
            if (ActionGroup == KSPActionGroup.RCS && f.Vessel == FlightGlobals.ActiveVessel)
            {
                FlightInputHandler.fetch.rcslock = !FlightInputHandler.RCSLock;
            }

            return false;
        }

        public static ActionGroupCommand WithGroup(KSPActionGroup group)
        {
            return new ActionGroupCommand
                {
                ActionGroup = group,
                TimeStamp = RTUtil.GameTime,
            };
        }
    }
}
