using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SanTint.Opc.Server.Model
{
    public class ADUSentControl
    {
        public bool NewDosingRequest { get; set; }
        public bool ConsumptionAccepted { get; set; }
        public bool ProcessOrderDataSent { get; set; }
    }

    public class ADUReceivedControl
    {
        public bool ReadyForNewDosing { get; set; }

        public bool DosingCompleted { get; set; }

        public bool RequestAccepted { get; set; }
    }
    
}
