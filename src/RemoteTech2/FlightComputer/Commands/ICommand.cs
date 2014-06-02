using System;

namespace RemoteTech
{
    public interface ICommand : IComparable<ICommand>
    {
        double TimeStamp { get; set; }
        double ExtraDelay { get; set; }
        string Description { get; }
        int Priority { get; }

        bool Pop(FlightComputer f);
        bool Execute(FlightComputer f, FlightCtrlState fcs);
        void Abort();
    }
}
