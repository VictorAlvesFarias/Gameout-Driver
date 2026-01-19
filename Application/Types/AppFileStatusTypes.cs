using System.ComponentModel;

namespace Application.Types
{
    public enum AppFileStatusTypes
    {
        [Description("Pending")]
        Pending = 1,

        [Description("Items in processing")]
        Processing = 2,

        [Description("Synced")]
        Synced = 3,

        [Description("Unsynced")]
        Unsynced = 4,

        [Description("Path not founded")]
        PathNotFounded = 5
    }
}
