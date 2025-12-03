using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Types
{
    public enum AppFileStatusTypes
    {
        Pending = 1,
        InProgress = 2,
        Synced = 3,
        Unsynced = 4,
        Error = 4,
    }
}
